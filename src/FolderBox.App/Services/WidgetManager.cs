using FolderBox.App.Shell;
using FolderBox.App.ViewModels;
using FolderBox.App.Views;
using FolderBox.Core.Logging;
using FolderBox.Core.Models;
using FolderBox.Core.Services;
using FolderBox.Core.Utilities;
using Microsoft.UI.Dispatching;

namespace FolderBox.App.Services;

/// <summary>
/// Owns the widget collection and every tile window plus the single expanded panel.
/// All members must be called on the UI thread.
/// </summary>
internal sealed class WidgetManager : IDisposable
{
    private const int ItemCountCap = 9999;

    private readonly PersistenceService _persistence;
    private readonly LayoutService _layout = new();
    private readonly DesktopIconService _desktopIcons = new();
    private readonly MonitorService _monitors;
    private readonly DesktopHostService _host;
    private readonly IconService _icons;
    private readonly ThemeService _theme;
    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<string, FolderTileWindow> _tiles = new();
    private readonly Dictionary<string, FolderWatcherService> _tileWatchers = new();
    private readonly Debouncer _saveDebouncer;
    private FoldersDocument _document = new();
    private FolderPanelWindow? _panel;
    private bool _disposed;

    public AppSettings Settings { get; private set; }
    public IReadOnlyList<FolderWidget> Widgets => _document.Folders;
    public MonitorService Monitors => _monitors;
    public DesktopHostService Host => _host;
    public IconService Icons => _icons;
    public ThemeService Theme => _theme;
    public LayoutService Layout => _layout;
    public DispatcherQueue Dispatcher => _dispatcher;

    /// <summary>Raised whenever the widget list or a widget's properties change.</summary>
    public event Action? WidgetsChanged;
    /// <summary>Raised after settings were applied.</summary>
    public event Action? SettingsChanged;

    public WidgetManager(PersistenceService persistence, MonitorService monitors, DesktopHostService host,
        IconService icons, ThemeService theme, DispatcherQueue dispatcher)
    {
        _persistence = persistence;
        _monitors = monitors;
        _host = host;
        _icons = icons;
        _theme = theme;
        _dispatcher = dispatcher;
        Settings = persistence.LoadSettings();
        _layout.GridProvider = (monitor, settings) =>
            settings.UseDesktopIconGrid ? (_desktopIcons.GetDesktopGrid(monitor) ?? LayoutService.DefaultGrid(monitor, settings)) : LayoutService.DefaultGrid(monitor, settings);
        _saveDebouncer = new Debouncer(TimeSpan.FromMilliseconds(400), () => _dispatcher.TryEnqueue(SaveNow));
        _monitors.MonitorsChanged += OnMonitorsChanged;
        _host.AppDeactivated += OnAppDeactivated;
        _host.ShellRestarted += OnShellRestarted;
    }

    /// <summary>Current lattice (cell and tile size) on a monitor — Windows' desktop icon grid or FolderBox's own.</summary>
    public GridSpec GridFor(MonitorInfo monitor) => _layout.GridFor(monitor, Settings);

    /// <summary>Blocked areas for layout decisions: Windows' own desktop icons (re-read on demand).</summary>
    private IReadOnlyList<LogicalRect> BlockedAreas(MonitorInfo monitor)
    {
        _desktopIcons.Refresh();
        return _desktopIcons.GetBlockedRects(monitor);
    }

    // ------------------------------------------------------------------ lifecycle

    public void Initialize()
    {
        _document = _persistence.LoadFolders();
        Log.Info($"Loaded {_document.Folders.Count} widget(s)");
        _desktopIcons.Refresh(force: true);
        if (_layout.NormalizeLayout(_document.Folders, _monitors.Monitors, Settings, BlockedAreas))
            SaveNow();

        foreach (var widget in _document.Folders)
            CreateTile(widget);
        _host.EnsureOrder();
        if (Directory.Exists(ManagedRoot)) EnsureRootIntegration();
    }

    /// <summary>
    /// Managed folders are not on the Desktop, so file dialogs (VS Code "Open Folder", ...) would not show them.
    /// Pin the root to Quick access (sidebar of every dialog) and give it the FolderBox icon.
    /// </summary>
    private void EnsureRootIntegration()
    {
        QuickAccessService.ApplyFolderIcon(ManagedRoot);
        if (Settings.PinRootToQuickAccess) QuickAccessService.Pin(ManagedRoot);
    }

    private FolderTileWindow CreateTile(FolderWidget widget)
    {
        var vm = new FolderTileViewModel(widget) { ShowItemCount = Settings.ShowItemCount };
        var window = new FolderTileWindow(vm, this);
        _tiles[widget.Id] = window;
        var monitor = _monitors.FindById(widget.MonitorId) ?? _monitors.Primary;
        window.PlaceAt(monitor, new LogicalPoint(widget.X, widget.Y));
        if (widget.IsVisible) window.ShowTile();

        // A lightweight watcher per tile keeps the item count live and notices when the folder vanishes.
        var watcher = new FolderWatcherService(TimeSpan.FromMilliseconds(800));
        watcher.Changed += () => _dispatcher.TryEnqueue(() => { if (_tiles.ContainsKey(widget.Id)) _ = UpdateItemCountAsync(window); });
        watcher.Failed += _ => _dispatcher.TryEnqueue(() => { if (_tiles.ContainsKey(widget.Id)) RefreshTileState(window); });
        _tileWatchers[widget.Id] = watcher;

        RefreshTileState(window);
        Log.Info($"Widget '{widget.DisplayName}' -> {widget.FolderPath} at ({widget.X:0},{widget.Y:0}) on {widget.MonitorId}");
        return window;
    }

    public void RefreshTileState(FolderTileWindow tile)
    {
        var vm = tile.ViewModel;
        var status = FolderService.GetStatus(vm.FolderPath);
        vm.IsAvailable = status is FolderStatus.Available or FolderStatus.AccessDenied;
        vm.StatusText = status switch
        {
            FolderStatus.NotFound => "Folder unavailable",
            FolderStatus.AccessDenied => "Access denied",
            FolderStatus.Error => "Cannot open",
            _ => string.Empty,
        };
        _ = UpdateItemCountAsync(tile);

        if (_tileWatchers.TryGetValue(vm.Id, out var watcher))
        {
            if (vm.IsAvailable) watcher.Watch(vm.FolderPath);
            else watcher.Stop();
        }
    }

    public async Task UpdateItemCountAsync(FolderTileWindow tile)
    {
        var vm = tile.ViewModel;
        if (!Settings.ShowItemCount || !vm.IsAvailable)
        {
            vm.ItemCountText = string.Empty;
            return;
        }
        try
        {
            var count = await FolderService.CountAsync(vm.FolderPath, ItemCountCap, CancellationToken.None);
            vm.ItemCountText = count switch
            {
                < 0 => string.Empty,
                0 => "Empty",
                1 => "1 item",
                >= ItemCountCap => $"{ItemCountCap:N0}+ items",
                _ => $"{count:N0} items",
            };
        }
        catch
        {
            vm.ItemCountText = string.Empty;
        }
    }

    public void RefreshAllTileStates()
    {
        foreach (var t in _tiles.Values) RefreshTileState(t);
    }

    // ------------------------------------------------------------------ add / remove / edit

    /// <summary>
    /// Shows the native folder picker and adds the chosen folder. <paramref name="near"/> (screen pixels)
    /// places the new tile in the free cell closest to that point — used by the "New > FolderBox" menu so
    /// the widget appears where the user right-clicked.
    /// </summary>
    public FolderWidget? AddFolderFromPicker(IntPtr ownerHwnd, string? initialFolder = null, PixelPoint? near = null)
    {
        var path = FolderPickerDialog.PickFolder(ownerHwnd, initialFolder: initialFolder);
        return path is null ? null : AddFolder(path, near: near);
    }

    /// <summary>Folders FolderBox creates itself ("New > FolderBox") live here; renaming such a widget renames the folder.</summary>
    public static string ManagedRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FolderBox");

    public static bool IsManagedFolder(string path)
    {
        var root = PathHelpers.NormalizeFolderPath(ManagedRoot);
        var dir = PathHelpers.NormalizeFolderPath(Path.GetDirectoryName(path) ?? string.Empty);
        return string.Equals(dir, root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a brand-new FolderBox: an empty real folder under <see cref="ManagedRoot"/> plus a widget
    /// placed next to <paramref name="near"/>, immediately in rename mode.
    /// </summary>
    public FolderWidget? CreateNewFolderBox(PixelPoint? near)
    {
        try
        {
            var rootExisted = Directory.Exists(ManagedRoot);
            Directory.CreateDirectory(ManagedRoot);
            if (!rootExisted) EnsureRootIntegration();
            var name = "New FolderBox";
            var path = Path.Combine(ManagedRoot, name);
            for (int i = 2; Directory.Exists(path) || File.Exists(path); i++)
            {
                name = $"New FolderBox ({i})";
                path = Path.Combine(ManagedRoot, name);
            }
            Directory.CreateDirectory(path);
            Log.Info($"Created new FolderBox folder {path}");
            var widget = AddFolder(path, name, near);
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => GetTile(widget.Id)?.BeginRename());
            return widget;
        }
        catch (Exception ex)
        {
            Log.Error("Could not create a new FolderBox", ex);
            return null;
        }
    }

    public FolderWidget AddFolder(string path, string? displayName = null, PixelPoint? near = null)
    {
        path = PathHelpers.NormalizeFolderPath(path);
        var monitor = near is { } np ? _monitors.FromPoint(np.X, np.Y) : _monitors.Primary;
        var onMonitor = _document.Folders.Where(f => string.Equals(f.MonitorId, monitor.Id, StringComparison.OrdinalIgnoreCase) && f.IsVisible).ToList();
        _desktopIcons.Refresh(force: true);
        LogicalPoint pos;
        if (near is { } p)
        {
            var grid = _layout.GridFor(monitor, Settings);
            var logical = monitor.ToLogical(p);
            // Centre the tile on the point, then let the layout find the nearest free cell.
            pos = _layout.ResolveDropPosition(new LogicalPoint(logical.X - grid.TileWidth / 2, logical.Y - grid.TileHeight / 2), monitor, onMonitor, Settings, BlockedAreas);
        }
        else
        {
            pos = _layout.SuggestNewPosition(onMonitor, monitor, Settings, BlockedAreas);
        }
        var widget = new FolderWidget
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? PathHelpers.GetDisplayNameForFolder(path) : displayName.Trim(),
            FolderPath = path,
            MonitorId = monitor.Id,
            X = pos.X,
            Y = pos.Y,
        };
        _document.Folders.Add(widget);
        CreateTile(widget);
        _host.EnsureOrder();
        SaveNow();
        WidgetsChanged?.Invoke();
        return widget;
    }

    /// <summary>
    /// Removes a widget from the UI. For a FolderBox-created folder that still has content, asks whether
    /// the content should go back to the Desktop (Yes: move everything to the Desktop and delete the
    /// emptied folder; No: keep the folder as it is; Cancel: do nothing). Nothing is ever deleted
    /// permanently — the folder either stays or is removed only once it is empty.
    /// </summary>
    public void RemoveWidgetInteractive(string id, IntPtr ownerHwnd)
    {
        var widget = _document.Folders.FirstOrDefault(f => f.Id == id);
        if (widget is null) return;

        if (IsManagedFolder(widget.FolderPath) && Directory.Exists(widget.FolderPath))
        {
            bool hasContent;
            try { hasContent = Directory.EnumerateFileSystemEntries(widget.FolderPath).Any(); }
            catch { hasContent = true; }

            if (hasContent)
            {
                var answer = NativeMethods.MessageBoxW(ownerHwnd,
                    $"\"{widget.DisplayName}\" was created by FolderBox and still contains items.\n\n" +
                    "Move everything back to the Desktop?\n\n" +
                    "Yes: move the contents to the Desktop and delete the emptied folder.\n" +
                    $"No: keep the folder ({widget.FolderPath}) as it is and only remove the tile.",
                    "Remove from FolderBox",
                    NativeMethods.MB_YESNOCANCEL | NativeMethods.MB_ICONQUESTION | NativeMethods.MB_SETFOREGROUND);
                if (answer == NativeMethods.IDCANCEL) return;
                if (answer == NativeMethods.IDYES)
                {
                    ClosePanel();
                    if (_tileWatchers.TryGetValue(id, out var w)) w.Stop();
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    var entries = Directory.EnumerateFileSystemEntries(widget.FolderPath).ToList();
                    var moved = ShellFileOperations.Transfer(entries, desktop, FileOperationKind.Move, ownerHwnd);
                    if (!moved)
                    {
                        RefreshTileState(GetTile(id)!);
                        return; // user aborted the move: keep the widget so nothing is lost
                    }
                }
            }

            // Empty FolderBox folder: clean it up so no stray "New FolderBox" folders accumulate.
            try
            {
                if (!Directory.EnumerateFileSystemEntries(widget.FolderPath).Any())
                {
                    if (_tileWatchers.TryGetValue(id, out var w)) w.Stop();
                    Directory.Delete(widget.FolderPath);
                    Log.Info($"Deleted empty FolderBox folder {widget.FolderPath}");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not delete empty folder {widget.FolderPath}: {ex.Message}");
            }
        }

        RemoveWidget(id);
    }

    /// <summary>Removes the widget only — the real folder is never touched.</summary>
    public void RemoveWidget(string id)
    {
        if (_panel?.CurrentTile?.ViewModel.Id == id) ClosePanel();
        if (_tiles.Remove(id, out var tile))
        {
            tile.CloseTile();
        }
        if (_tileWatchers.Remove(id, out var watcher)) watcher.Dispose();
        var removed = _document.Folders.RemoveAll(f => f.Id == id);
        if (removed > 0)
        {
            Log.Info($"Widget {id} removed (folder untouched)");
            SaveNow();
            WidgetsChanged?.Invoke();
        }
    }

    public void RenameWidget(string id, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrEmpty(newName)) return;
        if (!_tiles.TryGetValue(id, out var tile)) return;
        var widget = tile.ViewModel.Widget;

        // A FolderBox-created folder is the widget: keep the real folder name in sync.
        if (IsManagedFolder(widget.FolderPath)
            && string.Equals(Path.GetFileName(widget.FolderPath), widget.DisplayName, StringComparison.Ordinal))
        {
            var safe = string.Concat(newName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).TrimEnd('.', ' ');
            if (safe.Length > 0 && !string.Equals(safe, Path.GetFileName(widget.FolderPath), StringComparison.Ordinal))
            {
                var target = Path.Combine(ManagedRoot, safe);
                if (Directory.Exists(target) || File.Exists(target))
                {
                    Log.Warn($"Cannot rename folder: '{target}' already exists; only the widget name changes");
                }
                else
                {
                    try
                    {
                        if (_panel?.CurrentTile == tile) ClosePanel();
                        if (_tileWatchers.TryGetValue(id, out var w)) w.Stop();
                        Directory.Move(widget.FolderPath, target);
                        widget.FolderPath = target;
                        newName = safe;
                        Log.Info($"Renamed managed folder to {target}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Renaming the folder failed; only the widget name changes", ex);
                    }
                }
            }
        }

        widget.DisplayName = newName;
        tile.ViewModel.SyncFromModel();
        RefreshTileState(tile);
        if (_panel?.CurrentTile == tile) _panel.RefreshTitle();
        SaveDebounced();
        WidgetsChanged?.Invoke();
    }

    public void ChangeFolder(string id, string newPath)
    {
        if (!_tiles.TryGetValue(id, out var tile)) return;
        var widget = tile.ViewModel.Widget;
        newPath = PathHelpers.NormalizeFolderPath(newPath);
        var oldDefaultName = PathHelpers.GetDisplayNameForFolder(widget.FolderPath);
        if (string.Equals(widget.DisplayName, oldDefaultName, StringComparison.Ordinal))
            widget.DisplayName = PathHelpers.GetDisplayNameForFolder(newPath);
        widget.FolderPath = newPath;
        tile.ViewModel.SyncFromModel();
        if (_panel?.CurrentTile == tile) ClosePanel();
        RefreshTileState(tile);
        SaveNow();
        WidgetsChanged?.Invoke();
    }

    public void SetIconStyle(string id, string design, string color)
    {
        if (!_tiles.TryGetValue(id, out var tile)) return;
        var w = tile.ViewModel.Widget;
        w.IconStyle = IconStyles.NormalizeDesign(design);
        w.IconColor = string.IsNullOrWhiteSpace(color) ? IconStyles.DefaultColor : color;
        tile.ViewModel.SyncFromModel();
        if (_panel?.CurrentTile == tile) _panel.RefreshTitle();
        SaveDebounced();
        WidgetsChanged?.Invoke();
    }

    public void SetLocked(string id, bool locked)
    {
        if (!_tiles.TryGetValue(id, out var tile)) return;
        tile.ViewModel.Widget.IsLocked = locked;
        tile.ViewModel.SyncFromModel();
        SaveDebounced();
        WidgetsChanged?.Invoke();
    }

    public void SetVisible(string id, bool visible)
    {
        if (!_tiles.TryGetValue(id, out var tile)) return;
        tile.ViewModel.Widget.IsVisible = visible;
        if (visible) { tile.ShowTile(); _host.EnsureOrder(); }
        else
        {
            if (_panel?.CurrentTile == tile) ClosePanel();
            tile.HideTile();
        }
        SaveDebounced();
        WidgetsChanged?.Invoke();
    }

    public void ShowAll()
    {
        foreach (var w in _document.Folders) w.IsVisible = true;
        foreach (var t in _tiles.Values) t.ShowTile();
        _host.EnsureOrder();
        SaveDebounced();
        WidgetsChanged?.Invoke();
    }

    public void HideAll()
    {
        ClosePanel();
        foreach (var w in _document.Folders) w.IsVisible = false;
        foreach (var t in _tiles.Values) t.HideTile();
        SaveDebounced();
        WidgetsChanged?.Invoke();
    }

    public bool AllHidden => _document.Folders.Count > 0 && _document.Folders.All(f => !f.IsVisible);

    public bool IsEffectivelyLocked(FolderTileViewModel vm) => vm.IsLocked || Settings.LockAllWidgets;

    // ------------------------------------------------------------------ positioning

    /// <summary>Called by a tile when a drag ends. <paramref name="screenX"/>/<paramref name="screenY"/> are the tile's top-left in physical pixels.</summary>
    public void CommitTileMove(FolderTileWindow tile, int screenX, int screenY)
    {
        var widget = tile.ViewModel.Widget;
        // The monitor is chosen by the tile's centre so a drag across the monitor edge moves it over.
        var size = tile.AppWindow.Size;
        var centre = new PixelPoint(screenX + size.Width / 2, screenY + size.Height / 2);
        var monitor = _monitors.FromPoint(centre.X, centre.Y);
        var logical = monitor.ToLogical(new PixelPoint(screenX, screenY));
        var others = _document.Folders.Where(f => f.Id != widget.Id && string.Equals(f.MonitorId, monitor.Id, StringComparison.OrdinalIgnoreCase));
        _desktopIcons.Refresh(force: true);
        var resolved = _layout.ResolveDropPosition(logical, monitor, others, Settings, BlockedAreas);

        widget.MonitorId = monitor.Id;
        widget.X = resolved.X;
        widget.Y = resolved.Y;
        tile.PlaceAt(monitor, resolved);
        SaveDebounced();
    }

    private void OnMonitorsChanged()
    {
        Log.Info("Display configuration changed — re-validating widget positions");
        ClosePanel();
        _desktopIcons.Refresh(force: true);
        if (_layout.NormalizeLayout(_document.Folders, _monitors.Monitors, Settings, BlockedAreas)) SaveNow();
        foreach (var tile in _tiles.Values)
        {
            var w = tile.ViewModel.Widget;
            var monitor = _monitors.FindById(w.MonitorId) ?? _monitors.Primary;
            tile.PlaceAt(monitor, new LogicalPoint(w.X, w.Y));
        }
        _host.EnsureOrder();
    }

    private void OnShellRestarted()
    {
        foreach (var tile in _tiles.Values)
        {
            if (tile.ViewModel.Widget.IsVisible) tile.ShowTile();
        }
        _host.EnsureOrder();
        RefreshAllTileStates();
    }

    // ------------------------------------------------------------------ panel

    public bool IsPanelOpenFor(FolderTileWindow tile) => _panel is { IsOpen: true } && _panel.CurrentTile == tile;

    public void TogglePanel(FolderTileWindow tile)
    {
        if (IsPanelOpenFor(tile))
        {
            ClosePanel();
            return;
        }
        OpenPanel(tile);
    }

    public void OpenPanel(FolderTileWindow tile)
    {
        _panel ??= new FolderPanelWindow(new FolderPanelViewModel(_icons, _dispatcher), this);
        _panel.OpenFor(tile);
        _host.EnsureOrder();
    }

    public void ClosePanel()
    {
        _panel?.CloseIfOpen();
    }

    private void OnAppDeactivated()
    {
        if (Settings.ClosePanelOnOutsideClick && _panel is { IsOpen: true })
        {
            // Defer: a shell dialog (delete confirmation, context menu) briefly deactivates us too.
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_panel is { IsOpen: true } && !_panel.IsInteractionInProgress && !IsForegroundOurs())
                    ClosePanel();
            });
        }
    }

    private bool IsForegroundOurs()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(fg, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>Double click on a tile: classic behaviour, open the real folder in Explorer.</summary>
    public void OpenInExplorer(FolderTileWindow tile) => ShellLauncher.OpenInExplorer(tile.ViewModel.FolderPath);

    public void ItemCountMayHaveChanged(string folderPath)
    {
        foreach (var t in _tiles.Values)
        {
            if (string.Equals(t.ViewModel.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase))
                _ = UpdateItemCountAsync(t);
        }
    }

    // ------------------------------------------------------------------ settings

    public void ApplySettings(AppSettings updated)
    {
        var previous = Settings;
        Settings = updated.Clone();
        Settings.Normalize();
        _persistence.SaveSettings(Settings);

        if (previous.StartWithWindows != Settings.StartWithWindows)
            StartupService.SetEnabled(Settings.StartWithWindows);
        if (previous.Theme != Settings.Theme)
            _theme.Apply(Settings.Theme);
        if (previous.AddToNewMenu != Settings.AddToNewMenu)
        {
            if (Settings.AddToNewMenu) ShellNewIntegration.Register();
            else ShellNewIntegration.Unregister();
        }
        if (previous.PinRootToQuickAccess != Settings.PinRootToQuickAccess)
        {
            if (Settings.PinRootToQuickAccess) { Directory.CreateDirectory(ManagedRoot); EnsureRootIntegration(); }
            else QuickAccessService.Unpin(ManagedRoot);
        }
        if (previous.ShowItemCount != Settings.ShowItemCount)
        {
            foreach (var t in _tiles.Values)
            {
                t.ViewModel.ShowItemCount = Settings.ShowItemCount;
                _ = UpdateItemCountAsync(t);
            }
        }
        if (previous.AlignToGrid != Settings.AlignToGrid || previous.UseDesktopIconGrid != Settings.UseDesktopIconGrid
            || previous.GridWidth != Settings.GridWidth || previous.GridHeight != Settings.GridHeight)
        {
            OnMonitorsChanged();
        }
        if (previous.PanelWidth != Settings.PanelWidth || previous.PanelMaxHeight != Settings.PanelMaxHeight)
        {
            ClosePanel();
        }
        SettingsChanged?.Invoke();
    }

    // ------------------------------------------------------------------ persistence

    public void SaveDebounced() => _saveDebouncer.Trigger();

    public void SaveNow()
    {
        try
        {
            _persistence.SaveFolders(_document);
        }
        catch (Exception ex)
        {
            Log.Error("Saving folders failed", ex);
        }
    }

    public FolderTileWindow? GetTile(string id) => _tiles.GetValueOrDefault(id);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveDebouncer.Dispose();
        SaveNow();
        _panel?.Dispose();
        foreach (var w in _tileWatchers.Values) w.Dispose();
        _tileWatchers.Clear();
        foreach (var t in _tiles.Values) t.CloseTile();
        _tiles.Clear();
    }
}
