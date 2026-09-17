using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FolderBox.App.Shell;
using FolderBox.Core.Logging;
using FolderBox.Core.Models;
using FolderBox.Core.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace FolderBox.App.ViewModels;

internal sealed record BreadcrumbSegment(string Label, string Path, bool IsLast);

/// <summary>
/// State of the expanded panel: the widget it shows, the folder currently navigated to (root or a
/// sub-folder), its items and a live watcher that refreshes the list when the folder changes.
/// </summary>
internal sealed partial class FolderPanelViewModel : ObservableObject, IDisposable
{
    private readonly IconService _icons;
    private readonly DispatcherQueue _dispatcher;
    private readonly FolderWatcherService _watcher = new(TimeSpan.FromMilliseconds(250));
    private readonly Stack<string> _history = new();
    private CancellationTokenSource? _loadCts;
    private int _iconPhysicalSize = 48;

    public FolderTileViewModel? Tile { get; private set; }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _rootPath = string.Empty;
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _emptyMessage = string.Empty;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _isAtRoot = true;
    [ObservableProperty] private ImageSource? _icon;
    [ObservableProperty] private IReadOnlyList<FolderItemViewModel> _items = Array.Empty<FolderItemViewModel>();
    [ObservableProperty] private FolderItemViewModel? _selectedItem;
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = new();

    partial void OnSelectedItemChanged(FolderItemViewModel? oldValue, FolderItemViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    public int ItemCount => Items.Count;

    /// <summary>Raised on the UI thread after items were (re)loaded.</summary>
    public event Action? ItemsLoaded;

    public FolderPanelViewModel(IconService icons, DispatcherQueue dispatcher)
    {
        _icons = icons;
        _dispatcher = dispatcher;
        _watcher.Changed += () => _dispatcher.TryEnqueue(() => _ = LoadAsync(keepSelection: true));
        _watcher.Failed += ex => _dispatcher.TryEnqueue(() =>
        {
            Log.Warn($"Watcher failed for {CurrentPath}: {ex.Message}");
            _ = LoadAsync(keepSelection: true);
        });
    }

    public void SetIconScale(double scale) => _iconPhysicalSize = (int)Math.Round(48 * scale);

    /// <summary>Shows the root folder of a tile.</summary>
    public Task ShowAsync(FolderTileViewModel tile)
    {
        Tile = tile;
        Title = tile.DisplayName;
        RootPath = tile.FolderPath;
        _history.Clear();
        return NavigateAsync(tile.FolderPath);
    }

    public Task NavigateIntoAsync(string subfolderPath)
    {
        if (!string.IsNullOrEmpty(CurrentPath)) _history.Push(CurrentPath);
        return NavigateAsync(subfolderPath);
    }

    public Task GoBackAsync()
    {
        if (_history.Count == 0) return Task.CompletedTask;
        return NavigateAsync(_history.Pop());
    }

    public Task GoToBreadcrumbAsync(string path)
    {
        // Rebuild history so "back" still works naturally after jumping up.
        var newHistory = new List<string>();
        foreach (var h in _history.Reverse())
        {
            if (Core.Utilities.PathHelpers.IsSubPathOf(path, h) && !string.Equals(path, h, StringComparison.OrdinalIgnoreCase))
                newHistory.Add(h);
        }
        _history.Clear();
        foreach (var h in newHistory) _history.Push(h);
        return NavigateAsync(path);
    }

    private async Task NavigateAsync(string path)
    {
        CurrentPath = path;
        CanGoBack = _history.Count > 0;
        IsAtRoot = string.Equals(path, RootPath, StringComparison.OrdinalIgnoreCase);
        Title = IsAtRoot ? (Tile?.DisplayName ?? Title) : Core.Utilities.PathHelpers.GetDisplayNameForFolder(path);
        RebuildBreadcrumbs();
        _watcher.Watch(path);
        _ = LoadHeaderIconAsync(path);
        await LoadAsync(keepSelection: false);
    }

    public Task RefreshAsync() => LoadAsync(keepSelection: true);

    private async Task LoadAsync(bool keepSelection)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var path = CurrentPath;
        var selectedPath = keepSelection ? SelectedItem?.FullPath : null;
        IsLoading = Items.Count == 0;

        FolderListing listing;
        try
        {
            listing = await FolderService.ListAsync(path, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested || !ReferenceEquals(cts, _loadCts)) return;

        var vms = new List<FolderItemViewModel>(listing.Items.Count);
        foreach (var item in listing.Items) vms.Add(new FolderItemViewModel(item, _icons, _iconPhysicalSize));
        Items = vms;
        IsLoading = false;
        OnPropertyChanged(nameof(ItemCount));

        switch (listing.Status)
        {
            case FolderStatus.Available:
                IsEmpty = vms.Count == 0;
                EmptyMessage = "This folder is empty";
                Subtitle = vms.Count == 1 ? "1 item" : $"{vms.Count} items";
                if (Tile is not null && IsAtRoot) { Tile.IsAvailable = true; Tile.StatusText = string.Empty; }
                break;
            case FolderStatus.NotFound:
                IsEmpty = true;
                EmptyMessage = "Folder unavailable";
                Subtitle = "Not found";
                if (IsAtRoot && Tile is not null) { Tile.IsAvailable = false; Tile.StatusText = "Folder unavailable"; }
                break;
            case FolderStatus.AccessDenied:
                IsEmpty = true;
                EmptyMessage = "Access denied";
                Subtitle = "Access denied";
                break;
            default:
                IsEmpty = true;
                EmptyMessage = listing.ErrorMessage ?? "Could not read folder";
                Subtitle = "Error";
                break;
        }

        if (selectedPath is not null)
            SelectedItem = vms.FirstOrDefault(v => string.Equals(v.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase));
        else
            SelectedItem = null;

        ItemsLoaded?.Invoke();
    }

    private async Task LoadHeaderIconAsync(string path)
    {
        try
        {
            var img = await _icons.GetIconAsync(path, true, _iconPhysicalSize);
            if (string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase)) Icon = img;
        }
        catch { }
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        if (string.IsNullOrEmpty(CurrentPath)) return;
        var rel = CurrentPath.Length > RootPath.Length && CurrentPath.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase)
            ? CurrentPath.Substring(RootPath.Length).Trim(Path.DirectorySeparatorChar)
            : string.Empty;
        var segments = string.IsNullOrEmpty(rel) ? Array.Empty<string>() : rel.Split(Path.DirectorySeparatorChar);
        Breadcrumbs.Add(new BreadcrumbSegment(Title, RootPath, segments.Length == 0));
        var acc = RootPath;
        for (int i = 0; i < segments.Length; i++)
        {
            acc = Path.Combine(acc, segments[i]);
            Breadcrumbs.Add(new BreadcrumbSegment(segments[i], acc, i == segments.Length - 1));
        }
    }

    public void Close()
    {
        _loadCts?.Cancel();
        _watcher.Stop();
        Items = Array.Empty<FolderItemViewModel>();
        SelectedItem = null;
        Tile = null;
        _history.Clear();
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _watcher.Dispose();
    }
}
