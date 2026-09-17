using System.Runtime.InteropServices;
using FolderBox.Core.Logging;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Shell;

/// <summary>Opens files with their default handler and folders in Explorer — plain Shell "open" semantics.</summary>
internal static class ShellLauncher
{
    public static bool Open(string path, IntPtr ownerHwnd = default)
    {
        try
        {
            var info = new SHELLEXECUTEINFOW
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFOW>(),
                fMask = SEE_MASK_NOASYNC,
                hwnd = ownerHwnd,
                lpVerb = "open",
                lpFile = path,
                nShow = SW_SHOWNORMAL,
            };
            if (!ShellExecuteExW(ref info))
            {
                Log.Warn($"ShellExecute open failed for {path}: error {Marshal.GetLastWin32Error()}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Open failed for {path}", ex);
            return false;
        }
    }

    public static bool OpenInExplorer(string folderPath) => Open(folderPath);

    /// <summary>Opens Explorer at the parent folder with the item selected.</summary>
    public static bool RevealInExplorer(string path)
    {
        IntPtr pidl = IntPtr.Zero;
        try
        {
            pidl = ILCreateFromPathW(path);
            if (pidl == IntPtr.Zero) return Open(Path.GetDirectoryName(path) ?? path);
            return SHOpenFolderAndSelectItems(pidl, 0, null, 0) == 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"Reveal failed for {path}: {ex.Message}");
            return false;
        }
        finally
        {
            if (pidl != IntPtr.Zero) ILFree(pidl);
        }
    }

    public static void ShowProperties(string path, IntPtr ownerHwnd)
    {
        try
        {
            var info = new SHELLEXECUTEINFOW
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFOW>(),
                fMask = SEE_MASK_INVOKEIDLIST | SEE_MASK_NOASYNC,
                hwnd = ownerHwnd,
                lpVerb = "properties",
                lpFile = path,
                nShow = SW_SHOW,
            };
            ShellExecuteExW(ref info);
        }
        catch (Exception ex)
        {
            Log.Warn($"Properties failed for {path}: {ex.Message}");
        }
    }
}
