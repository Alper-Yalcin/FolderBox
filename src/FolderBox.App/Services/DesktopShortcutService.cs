using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FolderBox.Core.Logging;

namespace FolderBox.App.Services;

/// <summary>Creates the "FolderBox" launcher shortcut on the Desktop (IShellLink, no elevation).</summary>
internal static class DesktopShortcutService
{
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");

    public static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FolderBox.lnk");

    public static bool Exists() => File.Exists(ShortcutPath);

    /// <summary>Creates or refreshes the shortcut so it points at the running executable.</summary>
    public static bool Create()
    {
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unknown");
            var type = Type.GetTypeFromCLSID(ClsidShellLink) ?? throw new InvalidOperationException("ShellLink not available");
            var link = (IShellLinkW)Activator.CreateInstance(type)!;
            link.SetPath(exe);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe) ?? string.Empty);
            link.SetDescription("FolderBox - your desktop folders, expandable");
            link.SetIconLocation(exe, 0);
            ((IPersistFile)link).Save(ShortcutPath, true);
            Log.Info($"Desktop shortcut written to {ShortcutPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not create the desktop shortcut", ex);
            return false;
        }
    }
}
