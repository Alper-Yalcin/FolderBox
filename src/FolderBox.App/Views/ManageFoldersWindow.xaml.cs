using FolderBox.App.Services;
using FolderBox.App.Shell;
using FolderBox.App.ViewModels;
using FolderBox.Core.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Views;

/// <summary>
/// Admin panel: glass window with navigation (overview + every FolderBox), a card/list view of the
/// current folder that can be browsed into, and a details pane with the icon picker.
/// </summary>
internal sealed partial class ManageFoldersWindow : Window
{
    private readonly WidgetManager _manager;
    private readonly IntPtr _hwnd;
    private readonly Action _openSettings;
    private bool _renaming;
    private bool _syncingNav;
    private readonly List<ToggleButton> _designButtons = new();
    private readonly List<ToggleButton> _colorButtons = new();

    public ManageFoldersViewModel ViewModel { get; }


    public ManageFoldersWindow(WidgetManager manager, Action openSettings)
    {
        _manager = manager;
        _openSettings = openSettings;
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ViewModel = new ManageFoldersViewModel(manager, DispatcherQueue);

        SystemBackdrop = new PersistentAcrylicBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        manager.Theme.Register(Root);
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "FolderBox.ico"));

        var scale = manager.Monitors.Primary.Scale;
        AppWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(760 * scale)));
        CenterOnPrimary();
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
        {
            p.IsMaximizable = true;
            p.IsMinimizable = true;
        }

        BuildIconPickers();
        ViewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ManageFoldersViewModel.IsOverview):
                case nameof(ManageFoldersViewModel.IsGridView):
                    UpdateViewMode();
                    break;
                case nameof(ManageFoldersViewModel.Selected):
                case nameof(ManageFoldersViewModel.SelectedContent):
                case nameof(ManageFoldersViewModel.CurrentPath):
                    UpdateDetails();
                    UpdateHeaderIcon();
                    SyncNavigation();
                    break;
                case nameof(ManageFoldersViewModel.SearchText):
                case nameof(ManageFoldersViewModel.Items):
                    RefreshOverviewCards();
                    break;
            }
        };
        ViewModel.Items.CollectionChanged += (_, _) => RefreshOverviewCards();
        RefreshOverviewCards();
        UpdateViewMode();
        SyncNavigation();
        UpdateDetails();
        UpdateHeaderIcon();
        Closed += (_, _) => ViewModel.Dispose();
    }

    private void CenterOnPrimary()
    {
        var m = _manager.Monitors.Primary;
        var s = AppWindow.Size;
        AppWindow.Move(new PointInt32(m.WorkArea.Left + (m.WorkArea.Width - s.Width) / 2, m.WorkArea.Top + (m.WorkArea.Height - s.Height) / 2));
    }

    public void BringToFront()
    {
        Activate();
        SetForegroundWindow(_hwnd);
    }

    // ------------------------------------------------------------------ navigation

    private void SyncNavigation()
    {
        if (_syncingNav) return;
        _syncingNav = true;
        try
        {
            OverviewNav.SelectedIndex = ViewModel.IsOverview ? 0 : -1;
            BoxNav.SelectedItem = ViewModel.Selected;
        }
        finally { _syncingNav = false; }
    }

    private void OverviewNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingNav || OverviewNav.SelectedIndex < 0) return;
        ViewModel.ShowOverview();
    }

    private void BoxNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingNav) return;
        if (BoxNav.SelectedItem is ManageItemViewModel item && item != ViewModel.Selected) _ = ViewModel.OpenAsync(item);
        else if (BoxNav.SelectedItem is ManageItemViewModel same && ViewModel.IsOverview) _ = ViewModel.OpenAsync(same);
    }

    /// <summary>Shows exactly one of the four content presenters (overview/browse x cards/list).</summary>
    private void UpdateViewMode()
    {
        var overview = ViewModel.IsOverview;
        var grid = ViewModel.IsGridView;
        OverviewGrid.Visibility = overview && grid ? Visibility.Visible : Visibility.Collapsed;
        OverviewList.Visibility = overview && !grid ? Visibility.Visible : Visibility.Collapsed;
        ContentsGrid.Visibility = !overview && grid ? Visibility.Visible : Visibility.Collapsed;
        ContentsList.Visibility = !overview && !grid ? Visibility.Visible : Visibility.Collapsed;
        GridToggle.IsChecked = grid;
        ListToggle.IsChecked = !grid;
    }

    private void RefreshOverviewCards()
    {
        var selected = OverviewGrid.SelectedItem;
        var items = ViewModel.FilteredItems.ToList();
        OverviewGrid.ItemsSource = items;
        OverviewList.ItemsSource = items;
        if (selected is not null) { OverviewGrid.SelectedItem = selected; OverviewList.SelectedItem = selected; }
    }

    private void OverviewList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OverviewList.SelectedItem is ManageItemViewModel item && ViewModel.IsOverview) ShowDetailsFor(item);
    }

    private void OverviewGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OverviewGrid.SelectedItem is ManageItemViewModel item && ViewModel.IsOverview)
        {
            // Selecting a card previews it in the details pane without leaving the overview.
            ShowDetailsFor(item);
        }
    }

    private void OverviewGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ManageItemViewModel item) _ = ViewModel.OpenAsync(item);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _ = ViewModel.GoBackAsync();

    private void Breadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton { Tag: string path }) _ = ViewModel.GoToBreadcrumbAsync(path);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ViewModel.SearchText = sender.Text;

    private void GridToggle_Click(object sender, RoutedEventArgs e) { ViewModel.IsGridView = true; UpdateViewMode(); }
    private void ListToggle_Click(object sender, RoutedEventArgs e) { ViewModel.IsGridView = false; UpdateViewMode(); }

    // ------------------------------------------------------------------ contents

    private void Contents_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is FolderItemViewModel vm) { vm.EnsureIcon(); vm.EnsureDetails(); }
    }

    private void Contents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListViewBase list) ViewModel.SelectedContent = list.SelectedItem as FolderItemViewModel;
    }

    private void Contents_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not FolderItemViewModel vm) return;
        if (vm.IsDirectory) _ = ViewModel.NavigateIntoAsync(vm.FullPath);
        else ShellLauncher.Open(vm.FullPath, _hwnd);
    }

    private void Contents_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not FolderItemViewModel vm) return;
        ViewModel.SelectedContent = vm;
        if (sender is ListViewBase list) list.SelectedItem = vm;
        var subclass = _manager.Host.GetSubclass(_hwnd) ?? EnsureSubclass();
        GetCursorPos(out var pt);
        ShellContextMenu.Show(_hwnd, subclass, new[] { vm.FullPath }, pt.X, pt.Y, null);
        e.Handled = true;
    }

    private WindowSubclass? _subclass;
    private WindowSubclass EnsureSubclass() => _subclass ??= new WindowSubclass(_hwnd);

    private void NewSubfolder_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBrowsing || string.IsNullOrEmpty(ViewModel.CurrentPath)) { NewFolderBox_Click(sender, e); return; }
        try
        {
            var path = System.IO.Path.Combine(ViewModel.CurrentPath, "New folder");
            for (int i = 2; Directory.Exists(path) || File.Exists(path); i++)
                path = System.IO.Path.Combine(ViewModel.CurrentPath, $"New folder ({i})");
            Directory.CreateDirectory(path);
            _ = ViewModel.LoadContentsAsync(keepSelection: false);
        }
        catch (Exception ex)
        {
            Log.Error("New folder failed", ex);
        }
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBrowsing) return;
        var files = FolderPickerDialog.PickFiles(_hwnd);
        if (files.Count > 0) ShellFileOperations.Transfer(files, ViewModel.CurrentPath, FileOperationKind.Copy, _hwnd);
    }

    // ------------------------------------------------------------------ details pane

    private void UpdateHeaderIcon()
    {
        var s = ViewModel.Selected;
        HeaderIcon.Design = s?.IconStyle ?? IconStyles.DefaultDesign;
        HeaderIcon.Tint = s?.IconColor ?? IconStyles.DefaultColor;
        var hasBox = s is not null;
        MenuRename.Visibility = MenuLocate.Visibility = MenuExplorer.Visibility = MenuLock.Visibility = MenuShow.Visibility = MenuRemove.Visibility = MenuSep1.Visibility = MenuSep2.Visibility
            = hasBox ? Visibility.Visible : Visibility.Collapsed;
        if (s is not null)
        {
            MenuLock.IsChecked = s.IsLocked;
            MenuShow.IsChecked = s.IsVisible;
        }
        NewButton.Visibility = Visibility.Visible;
    }

    private void UpdateDetails()
    {
        if (ViewModel.SelectedContent is { } item)
        {
            ShowDetailsForItem(item);
            return;
        }
        if (ViewModel.Selected is { } box)
        {
            var atRoot = string.Equals(ViewModel.CurrentPath, box.FolderPath, StringComparison.OrdinalIgnoreCase);
            if (atRoot) ShowDetailsFor(box);
            else ShowDetailsForPath(ViewModel.CurrentPath, System.IO.Path.GetFileName(ViewModel.CurrentPath), "Folder");
            return;
        }
        // Overview totals
        DetailIcon.Visibility = Visibility.Visible;
        DetailImage.Visibility = Visibility.Collapsed;
        DetailIcon.Design = IconStyles.DefaultDesign;
        DetailIcon.Tint = IconStyles.DefaultColor;
        DetailName.Text = "FolderBox";
        DetailCount.Text = ViewModel.TotalWidgets == 1 ? "1 FolderBox" : $"{ViewModel.TotalWidgets} FolderBoxes";
        DetailDescription.Text = "Select a FolderBox to see its details, or double-click one to browse its contents.";
        DetailCreated.Text = "-";
        DetailModified.Text = "-";
        DetailType.Text = "Desktop";
        IconPicker.Visibility = Visibility.Collapsed;
    }

    private void ShowDetailsFor(ManageItemViewModel box)
    {
        DetailIcon.Visibility = Visibility.Visible;
        DetailImage.Visibility = Visibility.Collapsed;
        DetailIcon.Design = box.IconStyle;
        DetailIcon.Tint = box.IconColor;
        DetailName.Text = box.DisplayName;
        DetailCount.Text = box.ItemCountText;
        DetailDescription.Text = box.IsAvailable ? box.FolderPath : $"⚠ Folder unavailable • {box.FolderPath}";
        FillDates(box.FolderPath);
        DetailType.Text = box.IsManaged ? "FolderBox folder" : "Linked folder";
        IconPicker.Visibility = Visibility.Visible;
        foreach (var b in _designButtons)
        {
            b.IsChecked = string.Equals((string)b.Tag, box.IconStyle, StringComparison.OrdinalIgnoreCase);
            // Design previews follow the chosen colour so the picker shows what the tile will look like.
            if (b.Content is FolderBoxIcon preview) preview.Tint = box.IconColor;
        }
        foreach (var b in _colorButtons) b.IsChecked = string.Equals((string)b.Tag, box.IconColor, StringComparison.OrdinalIgnoreCase);
    }

    private void ShowDetailsForPath(string path, string name, string type)
    {
        DetailIcon.Visibility = Visibility.Visible;
        DetailImage.Visibility = Visibility.Collapsed;
        DetailIcon.Design = ViewModel.Selected?.IconStyle ?? IconStyles.DefaultDesign;
        DetailIcon.Tint = ViewModel.Selected?.IconColor ?? IconStyles.DefaultColor;
        DetailName.Text = name;
        DetailCount.Text = ViewModel.ContentsSummary;
        DetailDescription.Text = path;
        FillDates(path);
        DetailType.Text = type;
        IconPicker.Visibility = Visibility.Collapsed;
    }

    private void ShowDetailsForItem(FolderItemViewModel item)
    {
        item.EnsureIcon();
        item.EnsureDetails();
        DetailIcon.Visibility = Visibility.Collapsed;
        DetailImage.Visibility = Visibility.Visible;
        DetailImage.Source = item.Icon;
        if (item.Icon is null)
        {
            void Handler(object? s, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(FolderItemViewModel.Icon) && ViewModel.SelectedContent == item) DetailImage.Source = item.Icon;
                if (e.PropertyName == nameof(FolderItemViewModel.Subtitle) && ViewModel.SelectedContent == item) DetailCount.Text = item.Subtitle;
            }
            item.PropertyChanged += Handler;
        }
        DetailName.Text = item.Name;
        DetailCount.Text = item.IsDirectory ? item.Subtitle : item.Details;
        DetailDescription.Text = item.FullPath;
        FillDates(item.FullPath);
        DetailType.Text = item.TypeName;
        IconPicker.Visibility = Visibility.Collapsed;
    }

    private void FillDates(string path)
    {
        try
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                DetailCreated.Text = File.GetCreationTime(path).ToString("d MMM yyyy");
                DetailModified.Text = File.GetLastWriteTime(path).ToString("d MMM yyyy");
                return;
            }
        }
        catch { }
        DetailCreated.Text = "-";
        DetailModified.Text = "-";
    }

    private void BuildIconPickers()
    {
        foreach (var design in IconStyles.Designs)
        {
            var button = new ToggleButton { Tag = design.Name, Width = 46, Height = 46, Padding = new Thickness(5), CornerRadius = new CornerRadius(12), Content = new FolderBoxIcon { Design = design.Name, Width = 32, Height = 32 } };
            ToolTipService.SetToolTip(button, design.Label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, design.Label);
            button.Click += (_, _) => ApplyIcon(design.Name, null);
            _designButtons.Add(button);
            DesignList.Items.Add(button);
        }
        foreach (var swatch in IconStyles.Colors)
        {
            var color = swatch.Color ?? IconStyles.AccentColor();
            var button = new ToggleButton { Tag = swatch.Name, Width = 32, Height = 32, Padding = new Thickness(4), CornerRadius = new CornerRadius(16), Content = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(color) } };
            ToolTipService.SetToolTip(button, swatch.Label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, swatch.Label);
            button.Click += (_, _) => ApplyIcon(null, swatch.Name);
            _colorButtons.Add(button);
            ColorList.Items.Add(button);
        }
    }

    private void ApplyIcon(string? design, string? color)
    {
        var box = ViewModel.Selected ?? OverviewGrid.SelectedItem as ManageItemViewModel;
        if (box is null) return;
        _manager.SetIconStyle(box.Id, design ?? box.IconStyle, color ?? box.IconColor);
        ShowDetailsFor(box);
        UpdateHeaderIcon();
    }

    // ------------------------------------------------------------------ FolderBox actions

    private ManageItemViewModel? ActiveBox => ViewModel.Selected ?? (OverviewGrid.SelectedItem ?? OverviewList.SelectedItem) as ManageItemViewModel;

    private void NewFolderBox_Click(object sender, RoutedEventArgs e)
    {
        var widget = _manager.CreateNewFolderBox(null);
        if (widget is null) return;
        var item = ViewModel.Items.FirstOrDefault(i => i.Id == widget.Id);
        if (item is not null)
        {
            _ = ViewModel.OpenAsync(item);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, BeginRename);
        }
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var widget = _manager.AddFolderFromPicker(_hwnd);
        var item = widget is null ? null : ViewModel.Items.FirstOrDefault(i => i.Id == widget.Id);
        if (item is not null) _ = ViewModel.OpenAsync(item);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => _openSettings();

    private void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        var ok = DesktopShortcutService.Create();
        MessageBoxW(_hwnd, ok ? "A FolderBox shortcut was placed on your Desktop." : "The shortcut could not be created (see the log).", "FolderBox", 0x40);
    }

    private void Rename_Click(object sender, RoutedEventArgs e) => BeginRename();

    private void BeginRename()
    {
        var box = ViewModel.Selected;
        if (box is null || _renaming) return;
        _renaming = true;
        RenameBox.Text = box.DisplayName;
        NameText.Visibility = Visibility.Collapsed;
        RenameBox.Visibility = Visibility.Visible;
        RenameBox.Focus(FocusState.Programmatic);
        RenameBox.SelectAll();
    }

    private void EndRename(bool commit)
    {
        if (!_renaming) return;
        _renaming = false;
        RenameBox.Visibility = Visibility.Collapsed;
        NameText.Visibility = Visibility.Visible;
        var box = ViewModel.Selected;
        if (commit && box is not null && !string.IsNullOrWhiteSpace(RenameBox.Text))
            _manager.RenameWidget(box.Id, RenameBox.Text);
    }

    private void RenameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) { EndRename(true); e.Handled = true; }
        else if (e.Key == Windows.System.VirtualKey.Escape) { EndRename(false); e.Handled = true; }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e) => EndRename(true);

    private void Locate_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveBox is not { } box) return;
        var path = FolderPickerDialog.PickFolder(_hwnd, "Locate the folder", box.FolderPath);
        if (path is not null) _manager.ChangeFolder(box.Id, path);
    }

    private void OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBrowsing) ShellLauncher.OpenInExplorer(ViewModel.CurrentPath);
        else if (ActiveBox is { } box) ShellLauncher.OpenInExplorer(box.FolderPath);
    }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveBox is { } box) _manager.SetLocked(box.Id, MenuLock.IsChecked);
    }

    private void Show_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveBox is { } box) _manager.SetVisible(box.Id, MenuShow.IsChecked);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveBox is { } box) _manager.RemoveWidgetInteractive(box.Id, _hwnd);
    }
}
