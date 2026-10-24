using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace IconAdapt.Tests;

// Test-owned COM declarations: never call the production shortcut reader/writer for fixture
// setup or assertions. WScript.Shell uses the system ANSI code page for shortcut filenames;
// IShellLinkW and IPersistFile retain Chinese paths and properties on English Windows too.
internal static class ShortcutFixture
{
    internal static void CreateLink(string path, string target, string args, string working, string description, string icon)
        => WithLink(path, false, (link, persist) =>
        {
            link.SetPath(target);
            link.SetArguments(args);
            link.SetWorkingDirectory(working);
            link.SetDescription(description);
            if (icon.Length > 0)
            {
                int separator = icon.LastIndexOf(',');
                link.SetIconLocation(icon[..separator], int.Parse(icon[(separator + 1)..], CultureInfo.InvariantCulture));
            }
            persist.Save(path, true);
        });

    internal static (string Target, string Arguments, string WorkingDirectory, string Description, string Icon) ReadLink(string path)
    {
        (string, string, string, string, string) result = default;
        WithLink(path, true, (link, _) =>
        {
            var target = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            var working = new StringBuilder(32768);
            var description = new StringBuilder(32768);
            var icon = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH, no resolution or launch.
            link.GetArguments(arguments, arguments.Capacity);
            link.GetWorkingDirectory(working, working.Capacity);
            link.GetDescription(description, description.Capacity);
            link.GetIconLocation(icon, icon.Capacity, out int index);
            result = (target.ToString(), arguments.ToString(), working.ToString(), description.ToString(),
                icon + "," + index.ToString(CultureInfo.InvariantCulture));
        });
        return result;
    }

    internal static void EditArguments(string path, string arguments) => WithLink(path, true, (link, persist) =>
    {
        link.SetArguments(arguments);
        persist.Save(path, true);
    });

    private static void WithLink(string path, bool load, Action<IShellLinkW, IPersistFile> action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            object? instance = null;
            try
            {
                instance = new ShellLink();
                var persist = (IPersistFile)instance;
                if (load) persist.Load(path, 0);
                action((IShellLinkW)instance, persist);
            }
            catch (Exception ex) { error = ex; }
            finally { if (instance is not null) Marshal.FinalReleaseComObject(instance); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    // Complete native vtable order, including methods unused by the fixture.
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
}
