param(
    [Parameter(Mandatory = $true)][string] $Source,
    [Parameter(Mandatory = $true)][string] $Destination
)

$ErrorActionPreference = 'Stop'
if (-not ('Forma.Packaging.ExecutableBrand' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Forma.Packaging {
    public static class ExecutableBrand {
        private delegate bool NameCallback(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
        private delegate bool LanguageCallback(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumResourceNames(IntPtr module, IntPtr type, NameCallback callback, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumResourceLanguages(IntPtr module, IntPtr type, IntPtr name, LanguageCallback callback, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindResourceEx(IntPtr module, IntPtr type, IntPtr name, ushort language);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr BeginUpdateResource(string path, bool deleteExisting);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, byte[] data, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool EndUpdateResource(IntPtr update, bool discard);

        private sealed class Entry { public int Type; public int Name; public ushort Language; public byte[] Data; }
        private static List<Entry> Read(string path) {
            var module = LoadLibraryEx(path, IntPtr.Zero, 0x22);
            if (module == IntPtr.Zero) throw new Win32Exception();
            var entries = new List<Entry>();
            try {
                foreach (int kind in new[] { 3, 14, 16, 24 }) { // Icons, version and the app's asInvoker manifest.
                    EnumResourceNames(module, (IntPtr)kind, (m, type, name, p) => {
                        if ((name.ToInt64() >> 16) != 0) throw new InvalidOperationException("Expected numeric PE resource identifiers.");
                        if (!EnumResourceLanguages(m, type, name, (n, t, id, language, q) => {
                            var resource = FindResourceEx(n, t, id, language);
                            var data = new byte[checked((int)SizeofResource(n, resource))];
                            Marshal.Copy(LockResource(LoadResource(n, resource)), data, 0, data.Length);
                            entries.Add(new Entry { Type = kind, Name = id.ToInt32(), Language = language, Data = data });
                            return true;
                        }, IntPtr.Zero)) throw new Win32Exception();
                        return true;
                    }, IntPtr.Zero);
                }
            } finally { FreeLibrary(module); }
            return entries;
        }
        public static void Copy(string source, string destination) {
            var incoming = Read(source);
            if (!incoming.Exists(e => e.Type == 14) || !incoming.Exists(e => e.Type == 16) || !incoming.Exists(e => e.Type == 24))
                throw new InvalidOperationException("The app must supply its code-drawn icon, version and asInvoker manifest.");
            var outgoing = Read(destination);
            var update = BeginUpdateResource(destination, false);
            if (update == IntPtr.Zero) throw new Win32Exception();
            try {
                foreach (var entry in outgoing)
                    if (!UpdateResource(update, (IntPtr)entry.Type, (IntPtr)entry.Name, entry.Language, null, 0)) throw new Win32Exception();
                foreach (var entry in incoming)
                    if (!UpdateResource(update, (IntPtr)entry.Type, (IntPtr)entry.Name, entry.Language, entry.Data, (uint)entry.Data.Length)) throw new Win32Exception();
                if (!EndUpdateResource(update, false)) throw new Win32Exception();
                update = IntPtr.Zero;
            } finally { if (update != IntPtr.Zero) EndUpdateResource(update, true); }
        }
    }
}
'@
}
[Forma.Packaging.ExecutableBrand]::Copy([IO.Path]::GetFullPath($Source), [IO.Path]::GetFullPath($Destination))
