using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FolderBox.App.Services;
using FolderBox.App.Shell;
using FolderBox.Core.Models;
using FolderBox.Core.Services;
using FolderBox.Core.Utilities;

namespace FolderBox.App.ViewModels;

/// <summary>One FolderBox in the admin panel (navigation list and overview cards).</summary>
internal sealed partial class ManageItemViewModel : ObservableObject
{
    public string Id { get; }
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private bool _isManaged;
    [ObservableProperty] private string _iconStyle = IconStyles.DefaultDesign;
    [ObservableProperty] private string _iconColor = IconStyles.DefaultColor;
    [ObservableProperty] private string _itemCountText = string.Empty;

    public ManageItemViewModel(FolderWidget w)
    {
        Id = w.Id;
        Update(w);
    }

    public void Update(FolderWidget w)
    {
        DisplayName = w.DisplayName;
        FolderPath = w.FolderPath;
        IsLocked = w.IsLocked;
        IsVisible = w.IsVisible;
        IsAvailable = FolderService.GetStatus(w.FolderPath) != FolderStatus.NotFound;
        IsManaged = WidgetManager.IsManagedFolder(w.FolderPath);
        IconStyle = IconStyles.NormalizeDesign(w.IconStyle);
        IconColor = string.IsNullOrWhiteSpace(w.IconColor) ? IconStyles.DefaultColor : w.IconColor;
    }
}

/// <summary>
/// Admin panel state. Two modes: the overview (all FolderBoxes as cards) and browsing inside one
/// FolderBox (its folders/files as cards, with breadcrumb navigation into sub-folders).
/// </summary>
internal sealed partial class ManageFoldersViewModel : ObservableObject, IDisposable
{
    private readonly WidgetManager _manager;
    private readonly IconService _icons;
    private readonly FolderWatcherService _watcher = new(TimeSpan.FromMilliseconds(400));
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly Stack<string> _history = new();
    private CancellationTokenSource? _loadCts;
    private IReadOnlyList<FolderItemViewModel> _allContents = Array.Empty<FolderItemViewModel>();

    public ObservableCollection<ManageItemViewModel> Items { get; } = new();
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = new();

    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private ManageItemViewModel? _selected;
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private bool _isOverview = true;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private IReadOnlyList<FolderItemViewModel> _contents = Array.Empty<FolderItemViewModel>();
    [ObservableProperty] private FolderItemViewModel? _selectedContent;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isGridView = true;
    [ObservableProperty] private bool _contentsLoading;
    [ObservableProperty] private string _sectionTitle = "FolderBoxes";
    [ObservableProperty] private string _headerTitle = "FolderBox";
    [ObservableProperty] private string _headerSubtitle = string.Empty;
    [ObservableProperty] private string _contentsSummary = string.Empty;
    [ObservableProperty] private int _totalWidgets;

    public bool IsBrowsing => !IsOverview;
    public bool IsListView => !IsGridView;

    public ManageFoldersViewModel(WidgetManager manager, Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _manager = manager;
        _icons = manager.Icons;
        _dispatcher = dispatcher;
        _manager.WidgetsChanged += Refresh;
        _watcher.Changed += () => _dispatcher.TryEnqueue(() => _ = LoadContentsAsync(keepSelection: true));
        Refresh();
    }

    partial void OnIsOverviewChanged(bool value) => OnPropertyChanged(nameof(IsBrowsing));
    partial void OnIsGridViewChanged(bool value) => OnPropertyChanged(nameof(IsListView));
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    // ------------------------------------------------------------------ list of FolderBoxes

    public void Refresh()
    {
        var widgets = _manager.Widgets;
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (widgets.All(w => w.Id != Items[i].Id)) Items.RemoveAt(i);
        }
        for (int i = 0; i < widgets.Count; i++)
        {
            var w = widgets[i];
            var existing = Items.FirstOrDefault(x => x.Id == w.Id);
            if (existing is null) Items.Insert(Math.Min(i, Items.Count), new ManageItemViewModel(w));
            else existing.Update(w);
        }
        IsEmpty = Items.Count == 0;
        TotalWidgets = Items.Count;
        foreach (var item in Items) _ = UpdateCountAsync(item);

        if (Selected is not null && Items.All(i => i.Id != Selected.Id))
        {
            ShowOverview();
        }
        else if (Selected is not null)
        {
            Selected = Items.First(i => i.Id == Selected.Id);
            UpdateHeader();
        }
        else
        {
            UpdateHeader();
        }
    }

    private async Task UpdateCountAsync(ManageItemViewModel item)
    {
        if (!item.IsAvailable) { item.ItemCountText = "Unavailable"; return; }
        var count = await FolderService.CountAsync(item.FolderPath, 9999, CancellationToken.None);
        item.ItemCountText = FormatCount(count);
    }

    private static string FormatCount(int count) => count switch { < 0 => "", 0 => "Empty", 1 => "1 item", >= 9999 => "9,999+ items", _ => $"{count:N0} items" };

    // ------------------------------------------------------------------ navigation

    public void ShowOverview()
    {
        Selected = null;
        SelectedContent = null;
        IsOverview = true;
        CurrentPath = string.Empty;
        _history.Clear();
        CanGoBack = false;
        Breadcrumbs.Clear();
        _watcher.Stop();
        Contents = Array.Empty<FolderItemViewModel>();
        SectionTitle = "FolderBoxes";
        SearchText = string.Empty;
        UpdateHeader();
    }

    public Task OpenAsync(ManageItemViewModel item)
    {
        Selected = item;
        IsOverview = false;
        _history.Clear();
        SearchText = string.Empty;
        return NavigateAsync(item.FolderPath);
    }

    public Task NavigateIntoAsync(string path)
    {
        if (!string.IsNullOrEmpty(CurrentPath)) _history.Push(CurrentPath);
        return NavigateAsync(path);
    }

    public Task GoBackAsync()
    {
        if (_history.Count == 0) { ShowOverview(); return Task.CompletedTask; }
        return NavigateAsync(_history.Pop());
    }

    public Task GoToBreadcrumbAsync(string path)
    {
        var keep = _history.Reverse().Where(h => PathHelpers.IsSubPathOf(path, h) && !string.Equals(path, h, StringComparison.OrdinalIgnoreCase)).ToList();
        _history.Clear();
        foreach (var h in keep) _history.Push(h);
        return NavigateAsync(path);
    }

    private async Task NavigateAsync(string path)
    {
        CurrentPath = path;
        CanGoBack = true;
        SelectedContent = null;
        RebuildBreadcrumbs();
        _watcher.Watch(path);
        SectionTitle = string.Equals(path, Selected?.FolderPath, StringComparison.OrdinalIgnoreCase) ? "Contents" : PathHelpers.GetDisplayNameForFolder(path);
        UpdateHeader();
        await LoadContentsAsync(keepSelection: false);
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        if (Selected is null) return;
        var root = Selected.FolderPath;
        var rel = CurrentPath.Length > root.Length && CurrentPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? CurrentPath.Substring(root.Length).Trim(Path.DirectorySeparatorChar) : string.Empty;
        var segments = string.IsNullOrEmpty(rel) ? Array.Empty<string>() : rel.Split(Path.DirectorySeparatorChar);
        Breadcrumbs.Add(new BreadcrumbSegment(Selected.DisplayName, root, segments.Length == 0));
        var acc = root;
        for (int i = 0; i < segments.Length; i++)
        {
            acc = Path.Combine(acc, segments[i]);
            Breadcrumbs.Add(new BreadcrumbSegment(segments[i], acc, i == segments.Length - 1));
        }
    }

    private void UpdateHeader()
    {
        if (Selected is null)
        {
            HeaderTitle = "FolderBox";
            HeaderSubtitle = TotalWidgets == 1 ? "1 FolderBox on your desktop" : $"{TotalWidgets} FolderBoxes on your desktop";
        }
        else
        {
            HeaderTitle = Selected.DisplayName;
            HeaderSubtitle = string.IsNullOrEmpty(Selected.ItemCountText) ? Selected.FolderPath : $"{Selected.ItemCountText} • {Selected.FolderPath}";
        }
    }

    // ------------------------------------------------------------------ contents

    public async Task LoadContentsAsync(bool keepSelection)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var path = CurrentPath;
        var selectedPath = keepSelection ? SelectedContent?.FullPath : null;
        if (string.IsNullOrEmpty(path)) return;
        ContentsLoading = _allContents.Count == 0;
        try
        {
            var listing = await FolderService.ListAsync(path, cts.Token);
            if (cts.IsCancellationRequested) return;
            var scale = _manager.Monitors.Primary.Scale;
            _allContents = listing.Items.Select(i => new FolderItemViewModel(i, _icons, (int)Math.Round(48 * scale))).ToList();
            var folders = listing.Items.Count(i => i.IsDirectory);
            var files = listing.Items.Count - folders;
            var bytes = listing.Items.Where(i => i.IsFile).Sum(i => i.Size);
            ContentsSummary = listing.Status == FolderStatus.Available
                ? $"{folders} folder(s), {files} file(s) • {FileSizeFormatter.Format(bytes)}"
                : listing.ErrorMessage ?? "Could not read folder";
            ApplyFilter();
            if (selectedPath is not null)
                SelectedContent = Contents.FirstOrDefault(c => string.Equals(c.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase));
            if (Selected is not null && string.Equals(path, Selected.FolderPath, StringComparison.OrdinalIgnoreCase))
            {
                Selected.ItemCountText = FormatCount(listing.Items.Count);
                UpdateHeader();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(cts, _loadCts)) ContentsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        var q = SearchText?.Trim() ?? string.Empty;
        Contents = q.Length == 0
            ? _allContents
            : _allContents.Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Overview cards, optionally filtered by the search box.</summary>
    public IEnumerable<ManageItemViewModel> FilteredItems
    {
        get
        {
            var q = SearchText?.Trim() ?? string.Empty;
            return q.Length == 0 ? Items : Items.Where(i => i.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Dispose()
    {
        _manager.WidgetsChanged -= Refresh;
        _loadCts?.Cancel();
        _watcher.Dispose();
    }
}
