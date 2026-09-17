using System.Reflection;
using System.Runtime.InteropServices;
using FolderBox.Core.Logging;

namespace FolderBox.App.Services;

/// <summary>
/// Makes the managed FolderBox root easy to reach from every Open/Save dialog: pins it to Explorer's
/// Quick access (the sidebar of every common file dialog) and gives it the FolderBox icon via desktop.ini.
/// Uses the late-bound Shell.Application automation object; must be called on an STA thread.
/// </summary>
internal static class QuickAccessService
{
    private const string QuickAccessFolder = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";

    public static bool IsPinned(string folder)
    {
        try
        {
            var shell = CreateShell();
            if (shell is null) return false;
            var quickAccess = Invoke(shell, "NameSpace", QuickAccessFolder);
            if (quickAccess is null) return false;
            var items = Invoke(quickAccess, "Items");
            if (items is null) return false;
            var count = (int)(Invoke(items, "Count") ?? 0);
            var target = Path.TrimEndingDirectorySeparator(folder);
            for (int i = 0; i < count; i++)
            {
                var item = Invoke(items, "Item", i);
                if (item is null) continue;
                var path = Invoke(item, "Path") as string;
                if (path is not null && string.Equals(Path.TrimEndingDirectorySeparator(path), target, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read Quick access: {ex.Message}");
        }
        return false;
    }

    /// <summary>Pins <paramref name="folder"/> to Quick access (no-op when it is already there).</summary>
    public static bool Pin(string folder)
    {
        if (!Directory.Exists(folder)) return false;
        if (IsPinned(folder)) return true;
        // Explorer applies the verb asynchronously; a pin issued right after an unpin can be dropped, so verify and retry once.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (!InvokeVerb(folder, "pintohome")) return false;
            Thread.Sleep(300);
            if (IsPinned(folder)) { Log.Info($"Pinned {folder} to Quick access"); return true; }
        }
        Log.Warn($"Quick access pin for {folder} did not stick");
        return false;
    }

    public static bool Unpin(string folder)
    {
        if (!Directory.Exists(folder) || !IsPinned(folder)) return true;
        if (!InvokeVerb(folder, "unpinfromhome")) return false;
        Log.Info($"Unpinned {folder} from Quick access");
        return true;
    }

    /// <summary>Writes a desktop.ini so the root folder shows the FolderBox icon in Explorer and file dialogs.</summary>
    public static void ApplyFolderIcon(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var ini = Path.Combine(folder, "desktop.ini");
            var content = "[.ShellClassInfo]\r\n" +
                          $"IconResource={exe},0\r\n" +
                          "InfoTip=Folders created with FolderBox\r\n";
            if (File.Exists(ini) && File.ReadAllText(ini) == content && (File.GetAttributes(folder) & FileAttributes.ReadOnly) != 0)
                return;
            if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
            File.WriteAllText(ini, content);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
            // Explorer only honours desktop.ini in folders flagged read-only (or system); it does not make the folder read-only.
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
            // Tell Explorer (and its icon cache) that the folder's appearance changed.
            try { SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSH, folder, null); } catch { }
            Log.Info($"Applied FolderBox icon to {folder}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not apply the folder icon to {folder}: {ex.Message}");
        }
    }

    private static bool InvokeVerb(string folder, string verb)
    {
        try
        {
            var shell = CreateShell();
            var ns = shell is null ? null : Invoke(shell, "NameSpace", folder);
            var self = ns is null ? null : Invoke(ns, "Self");
            if (self is null) return false;
            Invoke(self, "InvokeVerb", verb);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Quick access '{verb}' failed for {folder}: {ex.Message}");
            return false;
        }
    }

    private const int SHCNE_UPDATEDIR = 0x00001000;
    private const uint SHCNF_PATHW = 0x0005;
    private const uint SHCNF_FLUSH = 0x1000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, string? dwItem1, string? dwItem2);

    private static object? CreateShell()
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        return type is null ? null : Activator.CreateInstance(type);
    }

    private static object? Invoke(object target, string member, params object[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, target, args);
}
