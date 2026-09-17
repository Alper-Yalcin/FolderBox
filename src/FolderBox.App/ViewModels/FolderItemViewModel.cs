using CommunityToolkit.Mvvm.ComponentModel;
using FolderBox.App.Shell;
using FolderBox.Core.Models;
using FolderBox.Core.Utilities;
using Microsoft.UI.Xaml.Media;

namespace FolderBox.App.ViewModels;

/// <summary>One row in the expanded panel. Icons load lazily the first time the row is realised.</summary>
internal sealed partial class FolderItemViewModel : ObservableObject
{
    private readonly IconService _icons;
    private readonly int _iconPhysicalSize;
    private bool _iconRequested;

    public FolderItem Item { get; }
    public string Name => Item.Name;
    public string FullPath => Item.FullPath;
    public bool IsDirectory => Item.IsDirectory;
    public string TypeName { get; }
    public string Details { get; }

    [ObservableProperty] private ImageSource? _icon;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private bool _isSelected;
    /// <summary>For folders: "N items" (lazy); for files: type and size.</summary>
    [ObservableProperty] private string _subtitle = string.Empty;
    private bool _detailsRequested;
    [ObservableProperty] private string _renameText = string.Empty;

    public FolderItemViewModel(FolderItem item, IconService icons, int iconPhysicalSize)
    {
        Item = item;
        _icons = icons;
        _iconPhysicalSize = iconPhysicalSize;
        TypeName = icons.GetTypeName(item.FullPath, item.IsDirectory);
        Details = item.IsDirectory ? TypeName : $"{TypeName} • {FileSizeFormatter.Format(item.Size)}";
        Subtitle = item.IsDirectory ? string.Empty : Details;
    }

    /// <summary>Folder cards show their item count; computed lazily when the card is realised.</summary>
    public void EnsureDetails()
    {
        if (_detailsRequested || !IsDirectory) return;
        _detailsRequested = true;
        _ = LoadCountAsync();
    }

    private async Task LoadCountAsync()
    {
        try
        {
            var count = await Core.Services.FolderService.CountAsync(FullPath, 9999, CancellationToken.None);
            Subtitle = count switch { < 0 => "Unavailable", 0 => "Empty", 1 => "1 item", >= 9999 => "9,999+ items", _ => $"{count:N0} items" };
        }
        catch { }
    }

    public string AutomationName => $"{Name}, {Details}";

    /// <summary>Called by the view when the row becomes visible.</summary>
    public void EnsureIcon()
    {
        if (_iconRequested) return;
        _iconRequested = true;
        _ = LoadIconAsync();
    }

    private async Task LoadIconAsync()
    {
        try
        {
            var img = await _icons.GetIconAsync(FullPath, IsDirectory, _iconPhysicalSize);
            if (img is not null) Icon = img;
        }
        catch
        {
            // Missing icon is cosmetic.
        }
    }
}
