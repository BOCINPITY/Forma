using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IconAdapt.Windows;

/// <summary>Reads the largest original ICO/resource frame without asking Shell to upscale it.</summary>
internal static class NativeIconReader
{
    private const int MaxFrameBytes = 16 * 1024 * 1024;
    private const int MaxTotalBytes = 32 * 1024 * 1024;
    private const long MaxTotalPixels = 32L * 1024 * 1024;
    private const int MaxDimension = 4096;
    internal sealed record Image(byte[] Png, int Width, int Height, bool IsNative)
    {
        internal IReadOnlyList<Image> Frames { get; init; } = [];
    }
    private sealed record Frame(byte[] Bytes, int Width, int Height, int Bits, bool IsPng);

    internal static Image? TryRead(string path, int iconIndex)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) return null;
            List<Frame> frames;
            if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                if (iconIndex != 0 || new FileInfo(path).Length > 64 * 1024 * 1024) return null;
                frames = ReadIco(File.ReadAllBytes(path));
            }
            else frames = ReadModule(path, iconIndex);
            var originals = new List<Image>();
            var decodedSizes = new HashSet<(int, int)>();
            long totalPixels = 0;
            int totalBytes = 0;
            foreach (var frame in frames.OrderByDescending(f => (long)f.Width * f.Height).ThenByDescending(f => f.Bits).ThenByDescending(f => f.IsPng))
            {
                if (originals.Count >= 64) break;
                long pixels = (long)frame.Width * frame.Height;
                if (decodedSizes.Contains((frame.Width, frame.Height)) || totalPixels + pixels > MaxTotalPixels) continue;
                try
                {
                    byte[]? png = null;
                    if (frame.IsPng)
                    {
                        // Keep straight-alpha PNG bytes exactly. HICON/GDI conversion and premature
                        // resizing are unnecessary for modern icon resources, including >256 px PNGs.
                        using var originalStream = new MemoryStream(frame.Bytes, false);
                        using var original = new Bitmap(originalStream);
                        if (original.Width == frame.Width && original.Height == frame.Height)
                            png = frame.Bytes;
                    }
                    else
                    {
                        // cx/cy=0 and no LR_DEFAULTSIZE uses the actual resource dimensions.
                        IntPtr handle = CreateIconFromResourceEx(frame.Bytes, (uint)frame.Bytes.Length, true, 0x30000, 0, 0, 0);
                        if (handle == IntPtr.Zero) continue;
                        try
                        {
                            using var icon = Icon.FromHandle(handle);
                            using var bitmap = icon.ToBitmap();
                            if (bitmap.Width != frame.Width || bitmap.Height != frame.Height) continue;
                            using var output = new MemoryStream();
                            bitmap.Save(output, ImageFormat.Png);
                            png = output.ToArray();
                        }
                        finally { DestroyIcon(handle); }
                    }
                    if (png is null || (long)totalBytes + png.Length > MaxTotalBytes) continue;
                    originals.Add(new(png, frame.Width, frame.Height, true));
                    decodedSizes.Add((frame.Width, frame.Height));
                    totalPixels += pixels;
                    totalBytes += png.Length;
                }
                catch (Exception e) when (e is ArgumentException or ExternalException or OutOfMemoryException) { }
            }
            if (originals.Count > 0) return originals[0] with { Frames = originals };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or ExternalException or OverflowException) { }
        return null;
    }

    private static List<Frame> ReadIco(byte[] bytes)
    {
        var frames = new List<Frame>();
        if (bytes.Length < 6 || U16(bytes, 0) != 0 || U16(bytes, 2) != 1) return frames;
        int count = U16(bytes, 4);
        if (count is < 1 or > 256 || bytes.Length < 6 + count * 16) return frames;
        int totalBytes = 0;
        for (int i = 0; i < count; i++)
        {
            int at = 6 + i * 16;
            uint size = U32(bytes, at + 8), offset = U32(bytes, at + 12);
            if (size is 0 or > MaxFrameBytes || offset < 6 + count * 16 || (ulong)offset + size > (ulong)bytes.Length ||
                (long)totalBytes + size > MaxTotalBytes) continue;
            var frame = ParseFrame(bytes.AsSpan((int)offset, (int)size).ToArray(), U16(bytes, at + 6));
            if (frame is not null) { frames.Add(frame); totalBytes += (int)size; }
        }
        return frames;
    }

    private static List<Frame> ReadModule(string path, int iconIndex)
    {
        var frames = new List<Frame>();
        // Resource-only, exclusive data mapping: never execute the application's code or DllMain.
        IntPtr module = LoadLibraryEx(path, IntPtr.Zero, 0x60);
        if (module == IntPtr.Zero) return frames;
        try
        {
            byte[]? group = null;
            if (iconIndex < 0)
            {
                if (iconIndex < -65535) return frames;
                group = ReadResource(module, (IntPtr)(-iconIndex), 14);
            }
            else if (iconIndex < 4096)
            {
                int number = 0;
                EnumResourceNames(module, (IntPtr)14, (m, _, name, _) =>
                {
                    if (number++ != iconIndex) return number <= 4096;
                    group = ReadResource(m, name, 14);
                    return false;
                }, IntPtr.Zero);
            }
            if (group is null || group.Length < 6 || U16(group, 0) != 0 || U16(group, 2) != 1) return frames;
            int count = U16(group, 4);
            if (count is < 1 or > 256 || group.Length < 6 + count * 14) return frames;
            int totalBytes = 0;
            for (int i = 0; i < count; i++)
            {
                int at = 6 + i * 14;
                byte[]? bytes = ReadResource(module, (IntPtr)U16(group, at + 12), 3);
                if (bytes is null || (long)totalBytes + bytes.Length > MaxTotalBytes) continue;
                var frame = ParseFrame(bytes, U16(group, at + 6));
                if (frame is not null) { frames.Add(frame); totalBytes += bytes.Length; }
            }
        }
        finally { FreeLibrary(module); }
        return frames;
    }

    private static Frame? ParseFrame(byte[] bytes, int bits)
    {
        int width, height;
        bool png = bytes.Length >= 33 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (png)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8)) != 13 ||
                !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return null;
            uint actualWidth = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16));
            uint actualHeight = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20));
            if (actualWidth is < 1 or > MaxDimension || actualHeight is < 1 or > MaxDimension) return null;
            width = (int)actualWidth;
            height = (int)actualHeight;
            int channels = bytes[25] switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
            if (channels == 0) return null;
            bits = bytes[24] * channels;
        }
        else
        {
            if (bytes.Length < 40 || U32(bytes, 0) is < 40 or > 124) return null;
            width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
            int doubledHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
            if (doubledHeight <= 0 || doubledHeight % 2 != 0) return null;
            height = doubledHeight / 2;
            bits = U16(bytes, 14);
        }
        if (width is < 1 or > MaxDimension || height is < 1 or > MaxDimension) return null;
        return new(bytes, width, height, bits, png);
    }

    private static byte[]? ReadResource(IntPtr module, IntPtr name, int type)
    {
        IntPtr resource = FindResource(module, name, (IntPtr)type);
        if (resource == IntPtr.Zero) return null;
        uint size = SizeofResource(module, resource);
        if (size is 0 or > MaxFrameBytes) return null;
        IntPtr memory = LockResource(LoadResource(module, resource));
        if (memory == IntPtr.Zero) return null;
        byte[] bytes = new byte[size];
        Marshal.Copy(memory, bytes, 0, (int)size);
        return bytes;
    }

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private delegate bool EnumResourceProc(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW")]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumResourceNamesW")]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResourceProc callback, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindResourceW")]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconFromResourceEx(byte[] bytes, uint size, [MarshalAs(UnmanagedType.Bool)] bool isIcon,
        uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
