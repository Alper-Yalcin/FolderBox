using System.Runtime.InteropServices;
using FolderBox.Core.Logging;
using Microsoft.Win32;

namespace FolderBox.App.Services;

/// <summary>
/// Adds "FolderBox" to Explorer's right-click "New" menu (desktop and any folder).
/// Implemented through a per-user file type (.folderbox) whose ShellNew\Command launches FolderBox with
/// the location the user right-clicked; no file is ever created and no elevation is needed (HKCU only).
/// </summary>
internal static class ShellNewIntegration
{
    private const string Extension = ".folderbox";
    private const string ProgId = "FolderBox.Widget";
    private const string ClassesRoot = @"Software\Classes";

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{Extension}\ShellNew");
            return key?.GetValue("Command") is string cmd && cmd.Contains(ExePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Creates (or updates, e.g. after the exe moved) the registration. Returns false on failure.</summary>
    public static bool Register()
    {
        try
        {
            var exe = ExePath();
            using (var ext = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{Extension}"))
            {
                ext.SetValue(string.Empty, ProgId);
                using var shellNew = ext.CreateSubKey("ShellNew");
                // %1 = full path of the "new file" the shell would have created; we only use its folder.
                shellNew.SetValue("Command", $"\"{exe}\" --new \"%1\"");
                shellNew.SetValue("ItemName", "FolderBox");
            }
            using (var progId = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ProgId}"))
            {
                progId.SetValue(string.Empty, "FolderBox");
                progId.SetValue("FriendlyTypeName", "FolderBox");
                using var icon = progId.CreateSubKey("DefaultIcon");
                icon.SetValue(string.Empty, $"\"{exe}\",0");
            }
            NotifyShell();
            Log.Info("Registered 'New > FolderBox' shell menu entry");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not register the New-menu entry", ex);
            return false;
        }
    }

    public static bool Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{Extension}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{ProgId}", throwOnMissingSubKey: false);
            NotifyShell();
            Log.Info("Removed 'New > FolderBox' shell menu entry");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not remove the New-menu entry", ex);
            return false;
        }
    }

    private static string ExePath() => Environment.ProcessPath ?? throw new InvalidOperationException("Process path unknown");

    private static void NotifyShell()
    {
        const int SHCNE_ASSOCCHANGED = 0x08000000;
        const uint SHCNF_IDLIST = 0;
        try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); } catch { }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
