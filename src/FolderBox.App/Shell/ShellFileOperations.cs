using System.Runtime.InteropServices;
using FolderBox.Core.Logging;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Shell;

internal enum FileOperationKind { Copy, Move }

/// <summary>
/// Thin wrapper over the Shell's IFileOperation: the same engine Explorer uses, so conflicts show the
/// native "Replace / Skip / Compare" dialog, deletes go to the Recycle Bin and everything is undoable.
/// All calls must be made from the UI (STA) thread.
/// </summary>
internal static class ShellFileOperations
{
    private const uint CommonFlags = FOF_ALLOWUNDO | FOFX_ADDUNDORECORD | FOF_NOCONFIRMMKDIR | FOF_WANTNUKEWARNING;

    public static bool Transfer(IEnumerable<string> sourcePaths, string destinationFolder, FileOperationKind kind, IntPtr ownerHwnd)
    {
        var sources = sourcePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (sources.Count == 0) return false;

        try
        {
            var op = CreateOperation(ownerHwnd, CommonFlags);
            var dest = ShellItem.FromPath(destinationFolder);
            foreach (var src in sources)
            {
                // Dropping a folder onto itself / its own parent is a no-op in Explorer; mirror that.
                if (string.Equals(Path.GetDirectoryName(src.TrimEnd('\\')), destinationFolder, StringComparison.OrdinalIgnoreCase) && kind == FileOperationKind.Move)
                    continue;
                var item = ShellItem.FromPath(src);
                if (kind == FileOperationKind.Move) op.MoveItem(item, dest, null, IntPtr.Zero);
                else op.CopyItem(item, dest, null, IntPtr.Zero);
            }
            op.PerformOperations();
            op.GetAnyOperationsAborted(out var aborted);
            Log.Info($"{kind} {sources.Count} item(s) -> {destinationFolder}{(aborted ? " (aborted by user)" : "")}");
            return !aborted;
        }
        catch (COMException ex) when (IsUserCancel(ex.HResult))
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"{kind} to {destinationFolder} failed", ex);
            return false;
        }
    }

    /// <summary>Sends items to the Recycle Bin (never a permanent delete).</summary>
    public static bool Delete(IEnumerable<string> paths, IntPtr ownerHwnd)
    {
        var list = paths.ToList();
        if (list.Count == 0) return false;
        try
        {
            var op = CreateOperation(ownerHwnd, CommonFlags | FOFX_RECYCLEONDELETE);
            foreach (var p in list) op.DeleteItem(ShellItem.FromPath(p), IntPtr.Zero);
            op.PerformOperations();
            op.GetAnyOperationsAborted(out var aborted);
            Log.Info($"Recycled {list.Count} item(s){(aborted ? " (aborted)" : "")}");
            return !aborted;
        }
        catch (COMException ex) when (IsUserCancel(ex.HResult))
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("Delete failed", ex);
            return false;
        }
    }

    public static bool Rename(string path, string newName, IntPtr ownerHwnd)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        try
        {
            var op = CreateOperation(ownerHwnd, CommonFlags);
            op.RenameItem(ShellItem.FromPath(path), newName, IntPtr.Zero);
            op.PerformOperations();
            op.GetAnyOperationsAborted(out var aborted);
            return !aborted;
        }
        catch (COMException ex) when (IsUserCancel(ex.HResult))
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Rename {path} -> {newName} failed", ex);
            return false;
        }
    }

    private static IFileOperation CreateOperation(IntPtr ownerHwnd, uint flags)
    {
        var type = Type.GetTypeFromCLSID(ComGuids.CLSID_FileOperation) ?? throw new InvalidOperationException("FileOperation CLSID not registered");
        var op = (IFileOperation)Activator.CreateInstance(type)!;
        op.SetOperationFlags(flags);
        if (ownerHwnd != IntPtr.Zero) op.SetOwnerWindow(ownerHwnd);
        return op;
    }

    private static bool IsUserCancel(int hr) => hr is E_ABORT or ERROR_CANCELLED_HR or COPYENGINE_E_USER_CANCELLED;
}
