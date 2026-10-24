using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace IconAdapt.Windows;

internal static class ShellInterop
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, IntPtr data, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport, Guid("45E2B4AE-B1C3-11D0-B92F-00A0C90312E1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkDataList
    {
        void AddDataBlock(IntPtr data);
        void CopyDataBlock(uint signature, out IntPtr data);
        void RemoveDataBlock(uint signature);
        void GetFlags(out uint flags);
        void SetFlags(uint flags);
    }

    internal sealed record LinkData(string Target, string Arguments, string WorkingDirectory, string Description,
        int ShowCommand, short Hotkey, string IconPath, int IconIndex, byte[] Pidl, uint Flags)
    {
        internal bool SameLaunch(LinkData other) => DifferentLaunchFields(other).Count == 0;

        internal List<string> DifferentLaunchFields(LinkData other)
        {
            var fields = new List<string>();
            if (Target != other.Target) fields.Add("启动目标");
            if (Arguments != other.Arguments) fields.Add("参数");
            if (WorkingDirectory != other.WorkingDirectory) fields.Add("工作目录");
            if (Description != other.Description) fields.Add("描述");
            if (ShowCommand != other.ShowCommand) fields.Add("窗口状态");
            if (Hotkey != other.Hotkey) fields.Add("快捷键");
            if (!Pidl.AsSpan().SequenceEqual(other.Pidl)) fields.Add("目标 ID（PIDL）");
            // MS-SHLLINK 2.1.1: HasRelativePath (0x8) denotes a StringData structure,
            // not a launch behavior flag. Shell Save can remove it for a cross-volume target.
            // Accept that normalization only while the absolute target, PIDL and launch fields above
            // still match. Icon/expanded-icon/Unicode structure flags may also change for a new ICO.
            // https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-shllink/ae350202-3ba9-4790-9e9e-98935f4ee5af
            // Every other flag, including RunAsUser and compatibility behavior, remains protected.
            if ((Flags & ~0x40C8u) != (other.Flags & ~0x40C8u)) fields.Add("启动标志");
            return fields;
        }
    }

    internal static LinkData Read(string path)
    {
        object instance = new ShellLink();
        try
        {
            ((IPersistFile)instance).Load(path, 0);
            var link = (IShellLinkW)instance;
            var target = new StringBuilder(32768);
            var args = new StringBuilder(32768);
            var work = new StringBuilder(32768);
            var description = new StringBuilder(32768);
            var icon = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH, never Resolve.
            link.GetArguments(args, args.Capacity);
            link.GetWorkingDirectory(work, work.Capacity);
            link.GetDescription(description, description.Capacity);
            link.GetShowCmd(out int show);
            link.GetHotkey(out short hotkey);
            link.GetIconLocation(icon, icon.Capacity, out int index);
            ((IShellLinkDataList)instance).GetFlags(out uint flags);
            link.GetIDList(out IntPtr pidl);
            byte[] id = [];
            if (pidl != IntPtr.Zero)
            {
                try
                {
                    int size = checked((int)ILGetSize(pidl));
                    if (size > 1024 * 1024) throw new InvalidDataException("快捷方式 IDList 过大。");
                    id = new byte[size];
                    Marshal.Copy(pidl, id, 0, size);
                }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
            return new(target.ToString(), args.ToString(), work.ToString(), description.ToString(), show,
                hotkey, icon.ToString(), index, id, flags);
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    internal static void WriteIcon(string path, string iconPath, int iconIndex)
    {
        object instance = new ShellLink();
        try
        {
            var persist = (IPersistFile)instance;
            persist.Load(path, 0);
            ((IShellLinkW)instance).SetIconLocation(iconPath, iconIndex);
            persist.Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    internal static bool IsAdvertised(string path)
    {
        var product = new StringBuilder(39);
        var feature = new StringBuilder(256);
        var component = new StringBuilder(39);
        return MsiGetShortcutTarget(path, product, feature, component) == 0;
    }

    internal static byte[]? ExtractPng(string path, LinkData data, string? originalShortcutPath = null)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            string iconSource = Environment.ExpandEnvironmentVariables(string.IsNullOrEmpty(data.IconPath) ? data.Target : data.IconPath);
            if (!string.IsNullOrEmpty(iconSource) && !Path.IsPathFullyQualified(iconSource))
                iconSource = Path.GetFullPath(iconSource, Path.GetDirectoryName(originalShortcutPath ?? path)!);
            if (!string.IsNullOrEmpty(iconSource))
            {
                // Request a 256 px native frame before falling back to the standard Shell sizes.
                // nIconSize LOWORD is the large size; HIWORD is the small size.
                int hr = SHDefExtractIcon(iconSource, data.IconIndex, 0, out handle, out IntPtr small, 256 | (32u << 16));
                if (small != IntPtr.Zero) DestroyIcon(small);
                if (hr != 0 && handle != IntPtr.Zero) { DestroyIcon(handle); handle = IntPtr.Zero; }
                if (handle == IntPtr.Zero)
                {
                    ExtractIconEx(iconSource, data.IconIndex, out handle, out small, 1);
                    if (small != IntPtr.Zero) DestroyIcon(small);
                }
            }
            if (handle == IntPtr.Zero)
            {
                var info = new ShFileInfo();
                if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), 0x100) != IntPtr.Zero)
                    handle = info.Icon;
            }
            if (handle == IntPtr.Zero) return null;
            using var icon = Icon.FromHandle(handle);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally { if (handle != IntPtr.Zero) DestroyIcon(handle); }
    }

    internal static void Notify(string path) => SHChangeNotify(0x2000, 0x5, path, IntPtr.Zero);

    internal static bool IsCloudPlaceholder(string path)
    {
        IntPtr find = FindFirstFile(path, out var data);
        if (find == new IntPtr(-1)) throw new IOException("无法检查重解析点。");
        try
        {
            // Cloud placeholders redirect storage, not the name to a different filesystem object.
            // Reject junctions, symbolic links and unknown tags; accept current Cloud/OneDrive tags.
            return (data.ReparseTag & 0xFFFF0FFFu) == 0x9000001Au || data.ReparseTag == 0x80000021u;
        }
        finally { FindClose(find); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindData
    {
        public uint Attributes;
        public FILETIME Creation;
        public FILETIME Access;
        public FILETIME Write;
        public uint SizeHigh;
        public uint SizeLow;
        public uint ReparseTag;
        public uint Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AlternateName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll")] private static extern uint ILGetSize(IntPtr pidl);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHDefExtractIconW")]
    private static extern int SHDefExtractIcon(string file, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string path, IntPtr second);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiGetShortcutTargetW")]
    private static extern uint MsiGetShortcutTarget(string shortcut, StringBuilder product, StringBuilder feature, StringBuilder component);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindFirstFileW", SetLastError = true)]
    private static extern IntPtr FindFirstFile(string file, out FindData data);
    [DllImport("kernel32.dll")] private static extern bool FindClose(IntPtr find);
}
