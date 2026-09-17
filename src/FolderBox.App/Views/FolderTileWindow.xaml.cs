using System.ComponentModel;
using FolderBox.App.Services;
using FolderBox.App.Shell;
using FolderBox.App.Utilities;
using FolderBox.App.ViewModels;
using FolderBox.Core.Logging;
using FolderBox.Core.Models;
using FolderBox.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Views;

/// <summary>
/// A collapsed folder tile: a tiny frameless transparent window that looks like a desktop icon.
/// Handles click (expand/collapse), double click (Explorer), drag to move, drop of files, the widget
/// context menu and inline rename.
/// </summary>
internal sealed partial class FolderTileWindow : Window, IDropHost
{
    // Glass card tints (over a transparent window, so the wallpaper shows through)
    private static readonly SolidColorBrush IdleBackground = new(Windows.UI.Color.FromArgb(0x3C, 0x14, 0x14, 0x20));
    private static readonly SolidColorBrush IdleBorder = new(Windows.UI.Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush HoverBackground = new(Windows.UI.Color.FromArgb(0x58, 0x20, 0x20, 0x30));
    private static readonly SolidColorBrush HoverBorder = new(Windows.UI.Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
    private SolidColorBrush ActiveBackground => new(WithAlpha(AccentColor(), 0x55));
    private SolidColorBrush ActiveBorder => new(WithAlpha(AccentColor(), 0xC0));

    private static Windows.UI.Color AccentColor()
    {
        try
        {
            if (Application.Current.Resources.TryGetValue("SystemAccentColorLight2", out var c) && c is Windows.UI.Color color) return color;
        }
        catch { }
        return Windows.UI.Color.FromArgb(0xFF, 0xB0, 0x80, 0xFF);
    }

    private static Windows.UI.Color WithAlpha(Windows.UI.Color c, byte a) => Windows.UI.Color.FromArgb(a, c.R, c.G, c.B);

    private readonly WidgetManager _manager;
    private MonitorInfo _monitor;
    private readonly IntPtr _hwnd;

    // Pointer state
    private bool _pressed;
    private bool _dragging;
    private POINT _pressCursor;
    private PointInt32 _pressWindowPos;
    private long _lastClickTicks;
    private bool _isDropTarget;
    private bool _renaming;
    private bool _shown;

    private ShellDropTarget? _dropTarget;

    public FolderTileViewModel ViewModel { get; }
    public IntPtr Hwnd => _hwnd;
    public double Scale => Root.XamlRoot?.RasterizationScale is > 0 and var s ? s : _monitor.Scale;
    public MonitorInfo Monitor => _monitor;

    public FolderTileWindow(FolderTileViewModel viewModel, WidgetManager manager)
    {
        ViewModel = viewModel;
        _manager = manager;
        _monitor = manager.Monitors.Primary;
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        SystemBackdrop = new TransparentBackdrop();
        manager.Host.Attach(this, DesktopLayer.Tiles);
        manager.Theme.Register(Root);

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Root.Loaded += (_, _) =>
        {
            TextShadowHelper.Attach(Label, ShadowHost);
            Root.XamlRoot.Changed += OnXamlRootChanged;
            _dropTarget ??= ShellDropTarget.Attach(_hwnd, this);
        };
        Activated += (_, _) => _dropTarget?.RegisterAll();
        Closed += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _dropTarget?.Dispose();
        };

        ApplyAll();
    }

    // ------------------------------------------------------------------ window management

    public void PlaceAt(MonitorInfo monitor, LogicalPoint logical)
    {
        _monitor = monitor;
        var pos = monitor.ToPhysical(logical);
        var grid = _manager.GridFor(monitor);
        var w = (int)Math.Round(grid.TileWidth * monitor.Scale);
        var h = (int)Math.Round(grid.TileHeight * monitor.Scale);
        AppWindow.MoveAndResize(new RectInt32(pos.X, pos.Y, w, h));
        ApplyCompactLayout(grid.TileWidth < 90 || grid.TileHeight < 100);
    }

    /// <summary>Desktop icon cells (~75x91) are smaller than FolderBox's default tile: tighten spacing and fonts.</summary>
    private void ApplyCompactLayout(bool compact)
    {
        TileContent.Margin = compact ? new Thickness(1, 1, 1, 0) : new Thickness(4, 4, 4, 2);
        IconHost.Width = IconHost.Height = compact ? 44 : 48;
        Label.FontSize = compact ? 11.5 : 12;
        Label.LineHeight = compact ? 14 : 16;
        Label.Margin = compact ? new Thickness(0, 2, 0, 0) : new Thickness(0, 4, 0, 0);
        CountLabel.FontSize = compact ? 10 : 11;
        CountLabel.Margin = new Thickness(0, compact ? 0 : 1, 0, 0);
        Highlight.Margin = new Thickness(compact ? 0 : 2);
        Highlight.CornerRadius = new CornerRadius(compact ? 10 : 12);
    }

    public void ShowTile()
    {
        if (!_shown)
        {
            _shown = true;
            AppWindow.Show(false);
        }
        else if (!AppWindow.IsVisible)
        {
            AppWindow.Show(false);
        }
    }

    public void HideTile()
    {
        if (AppWindow.IsVisible) AppWindow.Hide();
    }

    public void CloseTile()
    {
        try { _manager.Host.Detach(this); Close(); } catch (Exception ex) { Log.Warn("Tile close failed: " + ex.Message); }
    }

    public PixelRect ScreenRect
    {
        get
        {
            var p = AppWindow.Position;
            var s = AppWindow.Size;
            return new PixelRect(p.X, p.Y, s.Width, s.Height);
        }
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        // DPI changed (window moved to a monitor with another scale): re-apply size and reload the icon.
        var newScale = sender.RasterizationScale;
        if (Math.Abs(newScale - _monitor.Scale) > 0.01 && !_dragging)
        {
            var monitor = _manager.Monitors.FromWindow(_hwnd);
            var w = ViewModel.Widget;
            PlaceAt(monitor, new LogicalPoint(w.X, w.Y));
        }
    }

    // ------------------------------------------------------------------ view model -> visuals

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FolderTileViewModel.DisplayName):
                Label.Text = ViewModel.DisplayName;
                UpdateAutomation();
                break;
            case nameof(FolderTileViewModel.IconStyle):
            case nameof(FolderTileViewModel.IconColor):
                ApplyIconStyle();
                break;
            case nameof(FolderTileViewModel.IsAvailable):
            case nameof(FolderTileViewModel.StatusText):
                WarnBadge.Visibility = ViewModel.IsAvailable ? Visibility.Collapsed : Visibility.Visible;
                UpdateCount();
                UpdateAutomation();
                break;
            case nameof(FolderTileViewModel.ItemCountText):
            case nameof(FolderTileViewModel.ShowItemCount):
                UpdateCount();
                break;
            case nameof(FolderTileViewModel.IsExpanded):
            case nameof(FolderTileViewModel.IsHovered):
                UpdateHighlight();
                UpdateAutomation();
                break;
        }
    }

    private void ApplyIconStyle()
    {
        TileIcon.Design = ViewModel.IconStyle;
        TileIcon.Tint = ViewModel.IconColor;
    }

    private void ApplyAll()
    {
        Label.Text = ViewModel.DisplayName;
        ApplyIconStyle();
        WarnBadge.Visibility = ViewModel.IsAvailable ? Visibility.Collapsed : Visibility.Visible;
        UpdateCount();
        UpdateHighlight();
        UpdateAutomation();
    }

    private void UpdateCount()
    {
        var text = !ViewModel.IsAvailable ? ViewModel.StatusText : (ViewModel.ShowItemCount ? ViewModel.ItemCountText : string.Empty);
        CountLabel.Text = text;
        CountLabel.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateHighlight()
    {
        if (ViewModel.IsExpanded || _isDropTarget)
        {
            Highlight.Background = ActiveBackground;
            Highlight.BorderBrush = ActiveBorder;
        }
        else if (ViewModel.IsHovered)
        {
            Highlight.Background = HoverBackground;
            Highlight.BorderBrush = HoverBorder;
        }
        else
        {
            Highlight.Background = IdleBackground;
            Highlight.BorderBrush = IdleBorder;
        }
    }

    private void UpdateAutomation() => AutomationProperties.SetName(Root, ViewModel.AutomationName);

    private void Label_SizeChanged(object sender, SizeChangedEventArgs e) => TextShadowHelper.Update(Label, ShadowHost);

    // ------------------------------------------------------------------ pointer: hover / click / drag

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e) => ViewModel.IsHovered = true;

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed) ViewModel.IsHovered = false;
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_renaming) return;
        var props = e.GetCurrentPoint(Root).Properties;
        if (!props.IsLeftButtonPressed) return;

        _pressed = true;
        _dragging = false;
        GetCursorPos(out _pressCursor);
        _pressWindowPos = AppWindow.Position;
        Root.CapturePointer(e.Pointer);
        Root.Focus(FocusState.Pointer);
        e.Handled = true;
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed) return;
        if (!IsKeyDown(VK_LBUTTON))
        {
            // Button is no longer physically down (e.g. a drag/drop loop swallowed the release): abort.
            _pressed = false;
            _dragging = false;
            return;
        }
        GetCursorPos(out var cursor);
        var dx = cursor.X - _pressCursor.X;
        var dy = cursor.Y - _pressCursor.Y;

        if (!_dragging)
        {
            var threshold = Math.Max(GetSystemMetrics(SM_CXDRAG), 4) * Scale;
            if (Math.Abs(dx) < threshold && Math.Abs(dy) < threshold) return;
            if (_manager.IsEffectivelyLocked(ViewModel)) return;
            _dragging = true;
            _manager.ClosePanel();
        }

        AppWindow.Move(new PointInt32(_pressWindowPos.X + dx, _pressWindowPos.Y + dy));
        e.Handled = true;
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed) return;
        var wasDragging = _dragging;
        _pressed = false;
        _dragging = false;
        // Releasing capture raises PointerCaptureLost synchronously; state is already reset above.
        Root.ReleasePointerCapture(e.Pointer);
        e.Handled = true;

        if (wasDragging)
        {
            var pos = AppWindow.Position;
            _manager.CommitTileMove(this, pos.X, pos.Y);
            _manager.Host.EnsureOrder();
            return;
        }

        var now = Environment.TickCount64;
        var doubleClick = _lastClickTicks != 0 && now - _lastClickTicks <= GetDoubleClickTime();
        if (doubleClick)
        {
            // Second click of a double click: classic behaviour, and do not toggle the panel again.
            _lastClickTicks = 0;
            _manager.OpenInExplorer(this);
            return;
        }
        _lastClickTicks = now;
        if (!ViewModel.IsAvailable)
        {
            _manager.RefreshTileState(this);
            if (!ViewModel.IsAvailable) { ShowContextMenu(null); return; }
        }
        _manager.TogglePanel(this);
    }

    private void Root_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // Only relevant when capture is taken away mid-interaction (e.g. a modal dialog appears).
        if (!_pressed) return;
        _pressed = false;
        ViewModel.IsHovered = false;
        if (_dragging)
        {
            _dragging = false;
            var pos = AppWindow.Position;
            _manager.CommitTileMove(this, pos.X, pos.Y);
            _manager.Host.EnsureOrder();
        }
    }

    private void Root_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_renaming) return;
        ShowContextMenu(e.GetPosition(Root));
        e.Handled = true;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_renaming) return;
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter:
            case Windows.System.VirtualKey.Space:
                _manager.TogglePanel(this);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.F2:
                BeginRename();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Application:
                ShowContextMenu(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Escape:
                _manager.ClosePanel();
                e.Handled = true;
                break;
        }
    }

    // ------------------------------------------------------------------ context menu

    private void ShowContextMenu(Windows.Foundation.Point? at)
    {
        var menu = new MenuFlyout();
        var available = ViewModel.IsAvailable;

        var open = new MenuFlyoutItem { Text = "Open", IsEnabled = available, Icon = new FontIcon { Glyph = "" } };
        open.Click += (_, _) => _manager.OpenPanel(this);
        menu.Items.Add(open);

        var explorer = new MenuFlyoutItem { Text = "Open in Explorer", IsEnabled = available, Icon = new FontIcon { Glyph = "" } };
        explorer.Click += (_, _) => _manager.OpenInExplorer(this);
        menu.Items.Add(explorer);

        menu.Items.Add(new MenuFlyoutSeparator());

        var rename = new MenuFlyoutItem { Text = "Rename Widget", Icon = new FontIcon { Glyph = "" } };
        rename.Click += (_, _) => BeginRename();
        menu.Items.Add(rename);

        var change = new MenuFlyoutItem { Text = available ? "Change Folder…" : "Locate Folder…", Icon = new FontIcon { Glyph = "" } };
        change.Click += (_, _) =>
        {
            var path = FolderPickerDialog.PickFolder(_hwnd, available ? "Choose a different folder" : "Locate the folder", available ? ViewModel.FolderPath : null);
            if (path is not null) _manager.ChangeFolder(ViewModel.Id, path);
        };
        menu.Items.Add(change);

        var lockItem = new ToggleMenuFlyoutItem { Text = "Lock Position", IsChecked = ViewModel.IsLocked, Icon = new FontIcon { Glyph = "" } };
        lockItem.Click += (_, _) => _manager.SetLocked(ViewModel.Id, lockItem.IsChecked);
        menu.Items.Add(lockItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var remove = new MenuFlyoutItem { Text = "Remove from FolderBox", Icon = new FontIcon { Glyph = "" } };
        remove.Click += (_, _) => _manager.RemoveWidgetInteractive(ViewModel.Id, _hwnd);
        menu.Items.Add(remove);

        if (at is { } p) menu.ShowAt(Root, p);
        else menu.ShowAt(Root);
    }

    // ------------------------------------------------------------------ inline rename

    public void BeginRename()
    {
        if (_renaming) return;
        _renaming = true;
        RenameBox.Text = ViewModel.DisplayName;
        Label.Visibility = Visibility.Collapsed;
        ShadowHost.Visibility = Visibility.Collapsed;
        RenameBox.Visibility = Visibility.Visible;
        RenameBox.Focus(FocusState.Programmatic);
        RenameBox.SelectAll();
        SetForegroundWindow(_hwnd);
    }

    private void EndRename(bool commit)
    {
        if (!_renaming) return;
        _renaming = false;
        var text = RenameBox.Text;
        RenameBox.Visibility = Visibility.Collapsed;
        Label.Visibility = Visibility.Visible;
        ShadowHost.Visibility = Visibility.Visible;
        if (commit && !string.IsNullOrWhiteSpace(text) && text.Trim() != ViewModel.DisplayName)
            _manager.RenameWidget(ViewModel.Id, text);
    }

    private void RenameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) { EndRename(true); e.Handled = true; }
        else if (e.Key == Windows.System.VirtualKey.Escape) { EndRename(false); e.Handled = true; }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e) => EndRename(true);

    // ------------------------------------------------------------------ drop target (desktop / Explorer -> folder), native OLE

    string? IDropHost.DropTargetFolder => ViewModel.IsAvailable ? ViewModel.FolderPath : null;
    string IDropHost.DropTargetName => ViewModel.DisplayName;
    void IDropHost.OnDragHighlight(bool active) => SetDropHighlight(active);

    // In-process (XAML) drags: e.g. an item dragged out of another FolderBox panel onto this tile.
    private (List<string> Paths, FileOperationKind Kind)? _xamlDrop;

    private async void Root_DragEnter(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        _xamlDrop = null;
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        if (!ViewModel.IsAvailable) return;
        var deferral = e.GetDeferral();
        try
        {
            var paths = await ShellDragSource.TryGetInternalPathsAsync(e.DataView);
            if (paths is not null)
            {
                var mods = e.Modifiers;
                _xamlDrop = ShellDragSource.Prepare(paths, ViewModel.FolderPath,
                    mods.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Control),
                    mods.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Shift));
            }
            ApplyXamlDragFeedback(e);
        }
        finally { deferral.Complete(); }
    }

    private void Root_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        ApplyXamlDragFeedback(e);
        e.Handled = true;
    }

    private void ApplyXamlDragFeedback(Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (_xamlDrop is not { } d) { e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None; SetDropHighlight(false); return; }
        e.AcceptedOperation = d.Kind == FileOperationKind.Move ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move : Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = $"{(d.Kind == FileOperationKind.Move ? "Move" : "Copy")} to {ViewModel.DisplayName}";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
        SetDropHighlight(true);
    }

    private void Root_DragLeave(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        _xamlDrop = null;
        SetDropHighlight(false);
    }

    private void Root_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        var d = _xamlDrop;
        _xamlDrop = null;
        SetDropHighlight(false);
        if (d is null) return;
        e.Handled = true;
        ((IDropHost)this).OnDropped(d.Value.Paths, d.Value.Kind);
    }

    void IDropHost.OnDropped(IReadOnlyList<string> paths, FileOperationKind kind)
    {
        // Run after the OLE drop call returned so the shell progress dialog does not fight the drag loop.
        DispatcherQueue.TryEnqueue(() =>
        {
            ShellFileOperations.Transfer(paths, ViewModel.FolderPath, kind, _hwnd);
            _manager.ItemCountMayHaveChanged(ViewModel.FolderPath);
        });
    }

    private void SetDropHighlight(bool on)
    {
        if (_isDropTarget == on) return;
        _isDropTarget = on;
        UpdateHighlight();
    }
}
