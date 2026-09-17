using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FolderBox.Core.Logging;
using FolderBox.Core.Utilities;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Shell;

// Native OLE drag & drop. WinRT's DataPackage/StorageItems bridge cannot represent shortcuts (.lnk),
// virtual items and a few other shell objects, and a failure inside its data provider takes the whole
// process down. Using the Shell's own data object (CF_HDROP + Shell IDList) and a classic IDropTarget
// gives FolderBox exactly the behaviour Explorer has for every kind of item.

[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTarget
{
    [PreserveSig] int DragEnter(IDataObject pDataObj, uint grfKeyState, long pt, ref uint pdwEffect);
    [PreserveSig] int DragOver(uint grfKeyState, long pt, ref uint pdwEffect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(IDataObject pDataObj, uint grfKeyState, long pt, ref uint pdwEffect);
}

[ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTargetHelper
{
    [PreserveSig] int DragEnter(IntPtr hwndTarget, IDataObject pDataObject, ref POINT ppt, uint dwEffect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int DragOver(ref POINT ppt, uint dwEffect);
    [PreserveSig] int Drop(IDataObject pDataObject, ref POINT ppt, uint dwEffect);
    [PreserveSig] int Show([MarshalAs(UnmanagedType.Bool)] bool fShow);
}

internal static class OleConstants
{
    public const uint DROPEFFECT_NONE = 0, DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, DROPEFFECT_LINK = 4;
    public const int DRAGDROP_S_DROP = 0x00040100, DRAGDROP_S_CANCEL = 0x00040101, DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;
    public const uint MK_LBUTTON = 0x0001, MK_RBUTTON = 0x0002, MK_SHIFT = 0x0004, MK_CONTROL = 0x0008;
    public const short CF_HDROP = 15;
    public static readonly Guid CLSID_DragDropHelper = new("4657278A-411B-11d2-839A-00C04FD918D0");
    public static readonly Guid BHID_DataObject = new("B8C0BD9F-ED24-455c-83E6-D5390C4FE8C4");
    public static readonly Guid IID_IDataObject = new("0000010e-0000-0000-C000-000000000046");
    public static readonly Guid IID_IShellItemArray = new("b63ea76d-1f85-456f-a19c-48159efa858b");
}

internal static class OleNative
{
    [DllImport("ole32.dll")] public static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget pDropTarget);
    [DllImport("ole32.dll")] public static extern int RevokeDragDrop(IntPtr hwnd);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, System.Text.StringBuilder? lpszFile, uint cch);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);
    public delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    /// <summary>File-system paths carried by a data object (CF_HDROP), or an empty list.</summary>
    public static List<string> GetPaths(IDataObject data)
    {
        var result = new List<string>();
        var format = new FORMATETC { cfFormat = OleConstants.CF_HDROP, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        try
        {
            if (data.QueryGetData(ref format) != 0) return result;
            data.GetData(ref format, out var medium);
            try
            {
                var hDrop = medium.unionmember;
                if (hDrop == IntPtr.Zero) return result;
                var count = DragQueryFileW(hDrop, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < count; i++)
                {
                    var len = DragQueryFileW(hDrop, i, null, 0);
                    var sb = new System.Text.StringBuilder((int)len + 1);
                    if (DragQueryFileW(hDrop, i, sb, len + 1) > 0) result.Add(sb.ToString());
                }
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Reading CF_HDROP failed: " + ex.Message);
        }
        return result;
    }

    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

    public static IDropTargetHelper? CreateDropTargetHelper()
    {
        try
        {
            var t = Type.GetTypeFromCLSID(OleConstants.CLSID_DragDropHelper);
            return t is null ? null : Activator.CreateInstance(t) as IDropTargetHelper;
        }
        catch { return null; }
    }

}

/// <summary>
/// Fills a WinUI <see cref="Windows.ApplicationModel.DataTransfer.DataPackage"/> for dragging items out of
/// FolderBox. Regular files and folders travel as StorageItems (the bridge exposes them as CF_HDROP to
/// Explorer). Shortcut files (.lnk/.url/…) cannot be represented by WinRT StorageFile, so they are
/// offered as streamed virtual files that the target copies; on a successful drop FolderBox removes the
/// original, which yields move semantics (unless Ctrl was held). A private "FolderBox.Paths" format lets
/// FolderBox's own windows read the real paths directly (in-process drags bypass OLE).
/// Raw OLE DoDragDrop is deliberately not used: from WinUI threads the loop never sees pointer input,
/// from background threads it cannot capture the mouse at all.
/// </summary>
internal static class ShellDragSource
{
    public const string FolderBoxPathsFormat = "FolderBox.Paths";

    private static readonly HashSet<string> ShortcutExtensions = new(StringComparer.OrdinalIgnoreCase) { ".lnk", ".url", ".website", ".pif", ".appref-ms" };

    public static bool IsShortcut(string path) => ShortcutExtensions.Contains(Path.GetExtension(path));

    /// <summary>State of one outgoing drag.</summary>
    public sealed class DragSession
    {
        /// <summary>Set once a target actually pulled the bytes of a streamed (shortcut) file.</summary>
        public volatile bool ContentDelivered;
    }

    public static DragSession Populate(Windows.ApplicationModel.DataTransfer.DataPackage data, IReadOnlyList<string> paths)
    {
        var session = new DragSession();
        data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy | Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        data.SetData(FolderBoxPathsFormat, string.Join("\n", paths));

        data.SetDataProvider(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var items = new List<Windows.Storage.IStorageItem>();
                foreach (var p in paths)
                {
                    try
                    {
                        if (Directory.Exists(p)) items.Add(await Windows.Storage.StorageFolder.GetFolderFromPathAsync(p));
                        else if (File.Exists(p))
                        {
                            if (IsShortcut(p)) items.Add(await CreateStreamedCopyAsync(p, session));
                            else items.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(p));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"StorageItem unavailable for {p}: {ex.Message}");
                    }
                }
                if (items.Count > 0)
                {
                    try { request.SetData(items); }
                    catch (Exception ex) { Log.Warn("SetData(StorageItems) failed: " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("StorageItems provider failed: " + ex.Message);
            }
            finally
            {
                deferral.Complete();
            }
        });
        return session;
    }

    /// <summary>A virtual StorageFile whose content is the shortcut's bytes; targets copy it as a new file.</summary>
    private static async Task<Windows.Storage.StorageFile> CreateStreamedCopyAsync(string path, DragSession session)
    {
        var name = Path.GetFileName(path);
        return await Windows.Storage.StorageFile.CreateStreamedFileAsync(name, async request =>
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(path);
                using var writer = new Windows.Storage.Streams.DataWriter(request);
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
                request.Dispose(); // closing the request completes the streamed file
                session.ContentDelivered = true;
                Log.Debug($"Streamed {bytes.Length} bytes for {name}");
            }
            catch (Exception ex)
            {
                Log.Warn($"Streaming {path} failed: {ex.Message}");
                try { request.FailAndClose(Windows.Storage.StreamedFileFailureMode.Failed); } catch { }
            }
        }, null);
    }

    /// <summary>
    /// Paths of a drag that originated inside FolderBox (same process), or null. In-process XAML drags do
    /// not go through OLE, so the XAML Drop handlers use this instead of the native drop target.
    /// </summary>
    public static async Task<List<string>?> TryGetInternalPathsAsync(Windows.ApplicationModel.DataTransfer.DataPackageView view)
    {
        try
        {
            if (!view.Contains(FolderBoxPathsFormat)) return null;
            var text = await view.GetTextAsync(FolderBoxPathsFormat);
            var list = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            return list.Count > 0 ? list : null;
        }
        catch (Exception ex)
        {
            Log.Warn("Reading internal drag data failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>Filters paths that must not be dropped into <paramref name="targetFolder"/> and decides move vs copy.</summary>
    public static (List<string> Paths, FileOperationKind Kind)? Prepare(List<string> paths, string targetFolder, bool ctrl, bool shift)
    {
        paths = paths.Where(p => !string.Equals(p, targetFolder, StringComparison.OrdinalIgnoreCase)
                              && !PathHelpers.IsSubPathOf(targetFolder, p)
                              && !string.Equals(Path.GetDirectoryName(p), targetFolder, StringComparison.OrdinalIgnoreCase)).ToList();
        if (paths.Count == 0) return null;
        var kind = ctrl ? FileOperationKind.Copy : shift ? FileOperationKind.Move
                 : paths.All(p => PathHelpers.IsSameVolume(p, targetFolder)) ? FileOperationKind.Move : FileOperationKind.Copy;
        return (paths, kind);
    }
}

/// <summary>What a window that accepts drops must provide.</summary>
internal interface IDropHost
{
    /// <summary>Folder the items would go to, or null when drops are not possible right now.</summary>
    string? DropTargetFolder { get; }
    string DropTargetName { get; }
    void OnDragHighlight(bool active);
    void OnDropped(IReadOnlyList<string> paths, FileOperationKind kind);
}

/// <summary>
/// Classic IDropTarget for a FolderBox window: CF_HDROP in, Explorer semantics for the effect
/// (same volume = move, Ctrl = copy, Shift = move), Windows drag image via IDropTargetHelper.
/// </summary>
[ComVisible(true)]
internal sealed class ShellDropTarget : IDropTarget, IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly IDropHost _host;
    private readonly IDropTargetHelper? _helper = OleNative.CreateDropTargetHelper();
    private List<string> _paths = new();
    private FileOperationKind _kind;

    private ShellDropTarget(IntPtr hwnd, IDropHost host)
    {
        _hwnd = hwnd;
        _host = host;
    }

    /// <summary>
    /// Replaces WinUI's own drop target on the window (and its child windows) with ours.
    /// </summary>
    public static ShellDropTarget Attach(IntPtr hwnd, IDropHost host)
    {
        var target = new ShellDropTarget(hwnd, host);
        target.RegisterAll();
        return target;
    }

    private readonly List<IntPtr> _registeredWindows = new();

    /// <summary>
    /// Registers on the top-level window and on every child window (WinUI's input bridge windows). OLE
    /// resolves drop targets from the innermost window under the cursor upwards, and WinUI registers its
    /// own target on a child lazily, so we occupy those windows first and re-check later.
    /// </summary>
    public void RegisterAll()
    {
        var windows = new List<IntPtr> { _hwnd };
        OleNative.EnumChildWindows(_hwnd, (child, _) => { windows.Add(child); return true; }, IntPtr.Zero);
        var summary = new List<string>();
        foreach (var w in windows)
        {
            if (_registeredWindows.Contains(w)) continue;
            var revoked = OleNative.RevokeDragDrop(w) == 0;
            var hr = OleNative.RegisterDragDrop(w, this);
            if (hr >= 0) _registeredWindows.Add(w);
            summary.Add($"0x{w.ToInt64():X}:{GetClassName(w)}{(revoked ? "(replaced)" : "")}{(hr < 0 ? $"(hr=0x{hr:X8})" : "")}");
        }
        if (summary.Count > 0) Log.Debug($"Drop target registered on {string.Join(", ", summary)}");
    }

    private uint Evaluate(uint keys)
    {
        var folder = _host.DropTargetFolder;
        if (_paths.Count == 0 || folder is null) return OleConstants.DROPEFFECT_NONE;
        if ((keys & OleConstants.MK_CONTROL) != 0) _kind = FileOperationKind.Copy;
        else if ((keys & OleConstants.MK_SHIFT) != 0) _kind = FileOperationKind.Move;
        else _kind = _paths.All(p => PathHelpers.IsSameVolume(p, folder)) ? FileOperationKind.Move : FileOperationKind.Copy;
        return _kind == FileOperationKind.Move ? OleConstants.DROPEFFECT_MOVE : OleConstants.DROPEFFECT_COPY;
    }

    private static POINT ToPoint(long pt) => new() { X = unchecked((int)(pt & 0xFFFFFFFF)), Y = unchecked((int)(pt >> 32)) };

    public int DragEnter(IDataObject pDataObj, uint grfKeyState, long pt, ref uint pdwEffect)
    {
        try
        {
            var folder = _host.DropTargetFolder;
            _paths = OleNative.GetPaths(pDataObj);
            if (folder is not null)
            {
                // Never allow dropping a folder into itself or into one of its own children.
                _paths.RemoveAll(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase) || PathHelpers.IsSubPathOf(folder, p));
                // Items already in the target folder cannot be "moved" there.
                _paths.RemoveAll(p => string.Equals(Path.GetDirectoryName(p), folder, StringComparison.OrdinalIgnoreCase));
            }
            pdwEffect = Evaluate(grfKeyState);
            Log.Debug($"DragEnter on 0x{_hwnd.ToInt64():X}: {_paths.Count} path(s), effect={pdwEffect}");
            _host.OnDragHighlight(pdwEffect != OleConstants.DROPEFFECT_NONE);
            var p = ToPoint(pt);
            _helper?.DragEnter(_hwnd, pDataObj, ref p, pdwEffect);
        }
        catch (Exception ex)
        {
            Log.Warn("DragEnter failed: " + ex.Message);
            pdwEffect = OleConstants.DROPEFFECT_NONE;
        }
        return 0;
    }

    public int DragOver(uint grfKeyState, long pt, ref uint pdwEffect)
    {
        pdwEffect = Evaluate(grfKeyState);
        var p = ToPoint(pt);
        _helper?.DragOver(ref p, pdwEffect);
        return 0;
    }

    public int DragLeave()
    {
        _paths = new List<string>();
        _host.OnDragHighlight(false);
        _helper?.DragLeave();
        return 0;
    }

    public int Drop(IDataObject pDataObj, uint grfKeyState, long pt, ref uint pdwEffect)
    {
        pdwEffect = Evaluate(grfKeyState);
        var p = ToPoint(pt);
        _helper?.Drop(pDataObj, ref p, pdwEffect);
        _host.OnDragHighlight(false);
        if (pdwEffect != OleConstants.DROPEFFECT_NONE)
        {
            var paths = _paths;
            var kind = _kind;
            _paths = new List<string>();
            try { _host.OnDropped(paths, kind); }
            catch (Exception ex) { Log.Error("Drop handling failed", ex); }
        }
        return 0;
    }

    public void Dispose()
    {
        foreach (var w in _registeredWindows)
        {
            try { OleNative.RevokeDragDrop(w); } catch { }
        }
        _registeredWindows.Clear();
    }
}
