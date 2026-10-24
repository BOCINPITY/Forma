using System.Buffers.Binary;
using System.Windows.Media.Imaging;
using IconAdapt.App.ViewModels;

namespace IconAdapt.App.Imaging;

/// <summary>Reads the actual exported PNG frames instead of resizing the large presentation preview.</summary>
public static class OutputIconFrames
{
    public static IReadOnlyDictionary<int, BitmapSource> Decode(byte[] icon)
    {
        if (icon.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(2, 2)) != 1)
            throw new ArgumentException("输出图标文件无效，无法显示实际尺寸预览。");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(4, 2));
        if (count is < 1 or > 256 || icon.Length < 6 + 16 * count) throw new ArgumentException("输出图标目录不完整。");
        var frames = new Dictionary<int, BitmapSource>();
        for (var index = 0; index < count; index++)
        {
            var entry = icon.AsSpan(6 + 16 * index, 16);
            var size = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            var length = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8, 4));
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(12, 4));
            if (length == 0 || offset < 6 + 16 * count || (ulong)offset + length > (ulong)icon.Length)
                throw new ArgumentException("输出图标帧超出文件范围。");
            var frame = ShortcutItem.Decode(icon.AsSpan((int)offset, (int)length).ToArray());
            if (frame == null || frame.PixelWidth != size || frame.PixelHeight != height)
                throw new ArgumentException("输出图标尺寸与实际图像不一致。");
            if (size == height) frames[size] = frame;
        }
        if (frames.Count == 0) throw new ArgumentException("输出图标缺少可预览的尺寸。");
        return frames;
    }
}
