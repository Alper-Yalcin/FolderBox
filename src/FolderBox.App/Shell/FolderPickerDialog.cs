using System.Runtime.InteropServices;
using FolderBox.Core.Logging;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Shell;

/// <summary>Native Windows folder picker (IFileOpenDialog with FOS_PICKFOLDERS).</summary>
internal static class FolderPickerDialog
{
    /// <summary>Returns the chosen folder path, or null when cancelled.</summary>
    public static string? PickFolder(IntPtr ownerHwnd, string title = "Select a folder for FolderBox", string? initialFolder = null)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ComGuids.CLSID_FileOpenDialog) ?? throw new InvalidOperationException("FileOpenDialog not available");
            var dialog = (IFileOpenDialog)Activator.CreateInstance(type)!;
            dialog.GetOptions(out var options);
            options |= FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR | FOS_DONTADDTORECENT;
            dialog.SetOptions(options);
            dialog.SetTitle(title);
            dialog.SetOkButtonLabel("Select Folder");
            if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder))
            {
                try { dialog.SetFolder(ShellItem.FromPath(initialFolder)); } catch { }
            }

            var hr = dialog.Show(ownerHwnd);
            if (hr != 0) return null; // cancelled
            dialog.GetResult(out var item);
            return ShellItem.GetFileSystemPath(item);
        }
        catch (COMException ex) when (ex.HResult == ERROR_CANCELLED_HR)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Folder picker failed", ex);
            return null;
        }
    }

    /// <summary>Native multi-select file picker. Returns the chosen paths (empty when cancelled).</summary>
    public static IReadOnlyList<string> PickFiles(IntPtr ownerHwnd, string title = "Add files to FolderBox", string? initialFolder = null)
    {
        var result = new List<string>();
        try
        {
            var type = Type.GetTypeFromCLSID(ComGuids.CLSID_FileOpenDialog) ?? throw new InvalidOperationException("FileOpenDialog not available");
            var dialog = (IFileOpenDialog)Activator.CreateInstance(type)!;
            dialog.GetOptions(out var options);
            options |= FOS_ALLOWMULTISELECT | FOS_FORCEFILESYSTEM | FOS_FILEMUSTEXIST | FOS_NOCHANGEDIR | FOS_DONTADDTORECENT;
            dialog.SetOptions(options);
            dialog.SetTitle(title);
            dialog.SetOkButtonLabel("Add");
            if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder))
            {
                try { dialog.SetFolder(ShellItem.FromPath(initialFolder)); } catch { }
            }
            if (dialog.Show(ownerHwnd) != 0) return result;
            dialog.GetResults(out var items);
            items.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                items.GetItemAt(i, out var item);
                var path = ShellItem.GetFileSystemPath(item);
                if (!string.IsNullOrEmpty(path)) result.Add(path);
            }
        }
        catch (COMException ex) when (ex.HResult == ERROR_CANCELLED_HR)
        {
        }
        catch (Exception ex)
        {
            Log.Error("File picker failed", ex);
        }
        return result;
    }
}
