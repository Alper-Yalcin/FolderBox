using System.Numerics;
using FolderBox.App.Services;
using FolderBox.App.Shell;
using FolderBox.App.ViewModels;
using FolderBox.Core.Logging;
using FolderBox.Core.Models;
using FolderBox.Core.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Views;

/// <summary>
/// The single expanded panel. A frameless acrylic window with rounded corners that is positioned next
/// to the tile it belongs to (right / left / below / above depending on free space) and resized to
/// its content up to the configured maximum height.
/// </summary>
internal sealed partial class FolderPanelWindow : Window, IDisposable, IDropHost
{
    // Logical layout constants; keep in sync with the XAML (padding 16/14, header 52, divider block 23).
    private const int HeaderHeight = 14 + 52 + 23;
    private const int BreadcrumbHeight = 32;
    private const int CardWidth = 128 + 8;   // ItemWidth + item margin
    private const int CardHeight = 126 + 8;
    private const int SidePadding = 16;
    private const int BottomPadding = 16;
    private const int MinPanelHeight = 200;
    private const int Gap = 8;

    private readonly WidgetManager _manager;
    private readonly IntPtr _hwnd;
    private FolderTileWindow? _tile;
    private PanelDirection _direction;
    private MonitorInfo? _monitor;
    private bool _shownOnce;
    private FolderItemViewModel? _renamingItem;
    private ShellDropTarget? _dropTarget;

    public FolderPanelViewModel ViewModel { get; }
    public FolderTileWindow? CurrentTile => _tile;
    public bool IsOpen { get; private set; }

    /// <summary>True while a shell menu or dialog owned by the panel is up (deactivation must not close us).</summary>
    public bool IsInteractionInProgress { get; private set; }

    public FolderPanelWindow(FolderPanelViewModel viewModel, WidgetManager manager)
    {
        ViewModel = viewModel;
        _manager = manager;
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        SystemBackdrop = new PersistentAcrylicBackdrop();
        manager.Host.Attach(this, DesktopLayer.Panel, roundedCorners: true);
        manager.Theme.Register(Root);
        ElementCompositionPreview.SetIsTranslationEnabled(PanelRoot, true);

        // Clicking anywhere in the panel must make it the foreground window so keyboard input follows
        // the mouse. (Because our windows refuse Z-order changes, the default mouse activation is not
        // always applied, so we do it explicitly and regardless of whether a child handled the press.)
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => EnsureForeground()), handledEventsToo: true);
        Root.Loaded += (_, _) => _dropTarget ??= ShellDropTarget.Attach(_hwnd, this);
        Activated += (_, _) => _dropTarget?.RegisterAll();

        ViewModel.ItemsLoaded += OnItemsLoaded;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FolderPanelViewModel.IsAtRoot) or nameof(FolderPanelViewModel.CanGoBack)) FitToContent();
        };
    }

    private double Scale => Root.XamlRoot?.RasterizationScale is > 0 and var s ? s : (_monitor?.Scale ?? 1.0);

    private void EnsureForeground()
    {
        if (GetForegroundWindow() != _hwnd) SetForegroundWindow(_hwnd);
    }

    // ------------------------------------------------------------------ open / close

    public void OpenFor(FolderTileWindow tile)
    {
        if (_tile is not null && _tile != tile) _tile.ViewModel.IsExpanded = false;
        _tile = tile;
        _monitor = _manager.Monitors.FromPoint(tile.ScreenRect.Left + tile.ScreenRect.Width / 2, tile.ScreenRect.Top + tile.ScreenRect.Height / 2);
        tile.ViewModel.IsExpanded = true;
        IsOpen = true;
        HeaderIcon.Design = tile.ViewModel.IconStyle;
        HeaderIcon.Tint = tile.ViewModel.IconColor;

        ViewModel.SetIconScale(_monitor.Scale);
        _ = ViewModel.ShowAsync(tile.ViewModel);

        // Decide the side using the maximum height so it does not flip when content arrives.
        var settings = _manager.Settings;
        var scale = _monitor.Scale;
        var maxH = (int)Math.Round(settings.PanelMaxHeight * scale);
        var w = (int)Math.Round(settings.PanelWidth * scale);
        var initial = PanelPlacement.Place(tile.ScreenRect, w, maxH, _monitor.WorkArea, (int)Math.Round(Gap * scale));
        _direction = initial.Direction;

        var h = (int)Math.Round(Math.Min(settings.PanelMaxHeight, 320) * scale);
        var placed = PanelPlacement.Place(tile.ScreenRect, w, h, _monitor.WorkArea, (int)Math.Round(Gap * scale), _direction);
        AppWindow.MoveAndResize(new RectInt32(placed.Rect.Left, placed.Rect.Top, placed.Rect.Width, placed.Rect.Height));

        if (!_shownOnce)
        {
            _shownOnce = true;
            AppWindow.Show(true);
        }
        else
        {
            AppWindow.Show(true);
        }
        SetForegroundWindow(_hwnd);
        Animate(initial.Direction);
        ItemsList.Focus(FocusState.Programmatic);
        Log.Debug($"Panel opened for '{tile.ViewModel.DisplayName}' direction={_direction}");
    }

    public void CloseIfOpen()
    {
        if (!IsOpen) return;
        IsOpen = false;
        EndRename(commit: false);
        if (_tile is not null) _tile.ViewModel.IsExpanded = false;
        var tileHwnd = _tile?.Hwnd ?? IntPtr.Zero;
        _tile = null;
        AppWindow.Hide();
        ViewModel.Close();
        // Focus is intentionally not forced back to the tile: SetForegroundWindow here made the next
        // click on the tile get lost (see FolderTileWindow pointer handling).
        _ = tileHwnd;
        Log.Debug("Panel closed");
    }

    public void RefreshTitle()
    {
        if (_tile is null) return;
        if (ViewModel.IsAtRoot) ViewModel.Title = _tile.ViewModel.DisplayName;
        HeaderIcon.Design = _tile.ViewModel.IconStyle;
        HeaderIcon.Tint = _tile.ViewModel.IconColor;
    }

    private void OnItemsLoaded()
    {
        FitToContent();
        if (_pendingRenamePath is { } pending)
        {
            var item = ViewModel.Items.FirstOrDefault(i => string.Equals(i.FullPath, pending, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
            {
                _pendingRenamePath = null;
                ViewModel.SelectedItem = item;
                ItemsList.ScrollIntoView(item);
                BeginRename(item);
            }
        }
        if (_tile is not null) _manager.ItemCountMayHaveChanged(ViewModel.RootPath);
    }

    /// <summary>Resizes the window to its content (clamped) and re-anchors it to the tile.</summary>
    private void FitToContent()
    {
        if (!IsOpen || _tile is null || _monitor is null) return;
        var settings = _manager.Settings;
        var columns = Math.Max(1, (settings.PanelWidth - 2 * SidePadding + 8) / CardWidth);
        var rows = (int)Math.Ceiling(ViewModel.ItemCount / (double)columns);
        var logicalHeight = HeaderHeight + (ViewModel.IsAtRoot ? 0 : BreadcrumbHeight)
                          + (ViewModel.ItemCount == 0 ? 90 : rows * CardHeight) + BottomPadding;
        logicalHeight = Math.Clamp(logicalHeight, MinPanelHeight, settings.PanelMaxHeight);

        var scale = Scale;
        var w = (int)Math.Round(settings.PanelWidth * scale);
        var h = (int)Math.Round(logicalHeight * scale);
        var placed = PanelPlacement.Place(_tile.ScreenRect, w, h, _monitor.WorkArea, (int)Math.Round(Gap * scale), _direction);
        var current = AppWindow.Position;
        var size = AppWindow.Size;
        if (current.X != placed.Rect.Left || current.Y != placed.Rect.Top || size.Width != placed.Rect.Width || size.Height != placed.Rect.Height)
            AppWindow.MoveAndResize(new RectInt32(placed.Rect.Left, placed.Rect.Top, placed.Rect.Width, placed.Rect.Height));
    }

    private void Animate(PanelDirection direction)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(PanelRoot);
            var compositor = visual.Compositor;
            var duration = TimeSpan.FromMilliseconds(160);

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f);
            fade.Duration = duration;

            var offset = direction switch
            {
                PanelDirection.Right => new Vector3(-8f, 0f, 0f),
                PanelDirection.Left => new Vector3(8f, 0f, 0f),
                PanelDirection.Below => new Vector3(0f, -8f, 0f),
                _ => new Vector3(0f, 8f, 0f),
            };
            var slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0f, offset);
            slide.InsertKeyFrame(1f, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(new Vector2(0f, 0f), new Vector2(0f, 1f)));
            slide.Duration = duration;

            visual.StartAnimation("Opacity", fade);
            visual.StartAnimation("Translation", slide);
        }
        catch (Exception ex)
        {
            Log.Debug("Panel animation skipped: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ header / footer

    private void CollapseButton_Click(object sender, RoutedEventArgs e) => _manager.ClosePanel();

    private void BackButton_Click(object sender, RoutedEventArgs e) => _ = ViewModel.GoBackAsync();

    private void Breadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton { Tag: string path }) _ = ViewModel.GoToBreadcrumbAsync(path);
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var target = ViewModel.CurrentPath;
        IsInteractionInProgress = true;
        try
        {
            var files = FolderPickerDialog.PickFiles(_hwnd);
            if (files.Count > 0) ShellFileOperations.Transfer(files, target, FileOperationKind.Copy, _hwnd);
        }
        finally
        {
            IsInteractionInProgress = false;
            if (IsOpen) SetForegroundWindow(_hwnd);
        }
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var target = ViewModel.CurrentPath;
        IsInteractionInProgress = true;
        try
        {
            var folder = FolderPickerDialog.PickFolder(_hwnd, "Add a folder to FolderBox");
            if (folder is not null && !Core.Utilities.PathHelpers.IsSubPathOf(target, folder))
                ShellFileOperations.Transfer(new[] { folder }, target, FileOperationKind.Copy, _hwnd);
        }
        finally
        {
            IsInteractionInProgress = false;
            if (IsOpen) SetForegroundWindow(_hwnd);
        }
    }

    private string? _pendingRenamePath;

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = "New folder";
            var path = Path.Combine(ViewModel.CurrentPath, name);
            for (int i = 2; Directory.Exists(path) || File.Exists(path); i++)
                path = Path.Combine(ViewModel.CurrentPath, $"{name} ({i})");
            Directory.CreateDirectory(path);
            _pendingRenamePath = path; // the watcher refresh will select it and start renaming
            _ = ViewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error("New folder failed", ex);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        ShellLauncher.OpenInExplorer(ViewModel.CurrentPath);
    }

    // ------------------------------------------------------------------ list interaction

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Keep the view model's selection in sync explicitly (x:Bind TwoWay on SelectedItem is not reliable for typed items).
        ViewModel.SelectedItem = ItemsList.SelectedItem as FolderItemViewModel;
    }

    private void ItemsList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is FolderItemViewModel vm) vm.EnsureIcon();
    }

    private void ItemsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FolderItemViewModel vm)
        {
            OpenItem(vm);
            e.Handled = true;
        }
    }

    private void OpenItem(FolderItemViewModel item)
    {
        if (item.IsDirectory)
        {
            _ = ViewModel.NavigateIntoAsync(item.FullPath);
        }
        else
        {
            ShellLauncher.Open(item.FullPath, _hwnd);
        }
    }

    private void ItemsList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not FolderItemViewModel vm) return;
        ViewModel.SelectedItem = vm;
        e.Handled = true;
        ShowShellMenu(vm);
    }

    private void ShowShellMenu(FolderItemViewModel vm)
    {
        var subclass = _manager.Host.GetSubclass(_hwnd);
        if (subclass is null) return;
        GetCursorPos(out var pt);
        IsInteractionInProgress = true;
        try
        {
            ShellContextMenu.Show(_hwnd, subclass, new[] { vm.FullPath }, pt.X, pt.Y, () => BeginRename(vm));
        }
        finally
        {
            IsInteractionInProgress = false;
            // The shell may have activated Explorer/another window; refocus us so ESC keeps working.
            if (IsOpen) SetForegroundWindow(_hwnd);
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (_renamingItem is not null) EndRename(false);
            else _manager.ClosePanel();
            e.Handled = true;
        }
    }

    private void ItemsList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_renamingItem is not null) return;
        var selected = ViewModel.SelectedItem;
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter:
                if (selected is not null) { OpenItem(selected); e.Handled = true; }
                break;
            case Windows.System.VirtualKey.Delete:
                if (selected is not null) { DeleteItem(selected); e.Handled = true; }
                break;
            case Windows.System.VirtualKey.F2:
                if (selected is not null) { BeginRename(selected); e.Handled = true; }
                break;
            case Windows.System.VirtualKey.Back:
                if (ViewModel.CanGoBack) { _ = ViewModel.GoBackAsync(); e.Handled = true; }
                break;
            case Windows.System.VirtualKey.Application:
                if (selected is not null) { ShowShellMenu(selected); e.Handled = true; }
                break;
        }
    }

    private void DeleteItem(FolderItemViewModel item)
    {
        IsInteractionInProgress = true;
        try
        {
            ShellFileOperations.Delete(new[] { item.FullPath }, _hwnd);
        }
        finally
        {
            IsInteractionInProgress = false;
            if (IsOpen) SetForegroundWindow(_hwnd);
        }
    }

    // ------------------------------------------------------------------ inline rename

    private void BeginRename(FolderItemViewModel item)
    {
        EndRename(commit: false);
        _renamingItem = item;
        item.RenameText = item.Name;
        item.IsRenaming = true;
    }

    private void EndRename(bool commit)
    {
        var item = _renamingItem;
        if (item is null) return;
        _renamingItem = null;
        item.IsRenaming = false;
        var newName = item.RenameText.Trim();
        if (commit && newName.Length > 0 && !string.Equals(newName, item.Name, StringComparison.Ordinal))
        {
            IsInteractionInProgress = true;
            try { ShellFileOperations.Rename(item.FullPath, newName, _hwnd); }
            finally { IsInteractionInProgress = false; }
        }
        ItemsList.Focus(FocusState.Programmatic);
    }

    private void RenameBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && box.Visibility == Visibility.Visible)
        {
            box.Focus(FocusState.Programmatic);
            var dot = box.Text.LastIndexOf('.');
            box.Select(0, dot > 0 && _renamingItem?.IsDirectory == false ? dot : box.Text.Length);
        }
    }

    private void RenameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) { EndRename(true); e.Handled = true; }
        else if (e.Key == Windows.System.VirtualKey.Escape) { EndRename(false); e.Handled = true; }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_renamingItem is not null) EndRename(true);
    }

    // ------------------------------------------------------------------ drag out (panel -> desktop / Explorer / other tiles)

    private List<string>? _dragOutPaths;
    private ShellDragSource.DragSession? _dragSession;

    private void ItemsList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<FolderItemViewModel>().Select(i => i.FullPath).ToList();
        if (paths.Count == 0 || _renamingItem is not null) { e.Cancel = true; return; }
        _dragOutPaths = paths;
        _dragSession = ShellDragSource.Populate(e.Data, paths);
        Log.Debug($"Drag out started: {string.Join(", ", paths)}");
    }

    private void ItemsList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var paths = _dragOutPaths;
        var session = _dragSession;
        _dragOutPaths = null;
        _dragSession = null;
        Log.Debug($"Drag out completed: result={args.DropResult} contentDelivered={session?.ContentDelivered}");
        if (paths is null || session is null) return;

        // Shortcuts travel as streamed copies (see ShellDragSource). Targets report no result for virtual
        // files, so "the target pulled the bytes" is the success signal; finish the move by sending the
        // originals to the Recycle Bin — unless Ctrl asked for a copy, or the item is already gone
        // (an in-process drop moved it for real).
        if (!session.ContentDelivered || IsKeyDown(VK_CONTROL)) return;
        var leftovers = paths.Where(p => ShellDragSource.IsShortcut(p) && File.Exists(p)).ToList();
        if (leftovers.Count == 0) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            IsInteractionInProgress = true;
            try { ShellFileOperations.Delete(leftovers, _hwnd); }
            finally { IsInteractionInProgress = false; }
        });
    }

    // ------------------------------------------------------------------ drop into the current folder: in-process (XAML) drags

    private (List<string> Paths, FileOperationKind Kind)? _xamlDrop;

    private async void ItemsList_DragEnter(object sender, DragEventArgs e)
    {
        _xamlDrop = null;
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        var target = ((IDropHost)this).DropTargetFolder;
        if (target is null) return;
        var deferral = e.GetDeferral();
        try
        {
            var paths = await ShellDragSource.TryGetInternalPathsAsync(e.DataView);
            if (paths is not null)
            {
                var mods = e.Modifiers;
                _xamlDrop = ShellDragSource.Prepare(paths, target,
                    mods.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Control),
                    mods.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Shift));
            }
            ApplyXamlDropFeedback(e);
        }
        finally { deferral.Complete(); }
    }

    private void ItemsList_DragOver(object sender, DragEventArgs e)
    {
        ApplyXamlDropFeedback(e);
        e.Handled = true;
    }

    private void ApplyXamlDropFeedback(DragEventArgs e)
    {
        if (_xamlDrop is not { } d) { e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None; return; }
        e.AcceptedOperation = d.Kind == FileOperationKind.Move ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move : Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = $"{(d.Kind == FileOperationKind.Move ? "Move" : "Copy")} to {((IDropHost)this).DropTargetName}";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
    }

    private void ItemsList_DragLeave(object sender, DragEventArgs e) => _xamlDrop = null;

    private void ItemsList_Drop(object sender, DragEventArgs e)
    {
        var d = _xamlDrop;
        _xamlDrop = null;
        if (d is null) return;
        e.Handled = true;
        ((IDropHost)this).OnDropped(d.Value.Paths, d.Value.Kind);
    }

    // ------------------------------------------------------------------ drop into the current folder, native OLE (drags from Explorer / other apps)

    string? IDropHost.DropTargetFolder => IsOpen && !string.IsNullOrEmpty(ViewModel.CurrentPath) && Directory.Exists(ViewModel.CurrentPath) ? ViewModel.CurrentPath : null;
    string IDropHost.DropTargetName => Path.GetFileName(ViewModel.CurrentPath) is { Length: > 0 } n ? n : ViewModel.Title;
    void IDropHost.OnDragHighlight(bool active) { }

    void IDropHost.OnDropped(IReadOnlyList<string> paths, FileOperationKind kind)
    {
        var target = ViewModel.CurrentPath;
        DispatcherQueue.TryEnqueue(() =>
        {
            IsInteractionInProgress = true;
            try { ShellFileOperations.Transfer(paths, target, kind, _hwnd); }
            finally { IsInteractionInProgress = false; }
        });
    }

    public void Dispose()
    {
        _dropTarget?.Dispose();
        ViewModel.ItemsLoaded -= OnItemsLoaded;
        ViewModel.Dispose();
        try { _manager.Host.Detach(this); Close(); } catch { }
    }
}
