using CommunityToolkit.Mvvm.ComponentModel;
using FolderBox.Core.Models;
using Microsoft.UI.Xaml.Media;

namespace FolderBox.App.ViewModels;

/// <summary>Presentation state of one collapsed desktop tile.</summary>
internal sealed partial class FolderTileViewModel : ObservableObject
{
    public FolderWidget Widget { get; }

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private string _iconStyle = string.Empty;
    [ObservableProperty] private string _iconColor = string.Empty;
    [ObservableProperty] private bool _isAvailable = true;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _itemCountText = string.Empty;
    [ObservableProperty] private bool _showItemCount = true;
    [ObservableProperty] private ImageSource? _icon;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isHovered;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _renameText = string.Empty;

    public string Id => Widget.Id;

    public FolderTileViewModel(FolderWidget widget)
    {
        Widget = widget;
        SyncFromModel();
    }

    public void SyncFromModel()
    {
        DisplayName = Widget.DisplayName;
        FolderPath = Widget.FolderPath;
        IsLocked = Widget.IsLocked;
        IconStyle = Widget.IconStyle;
        IconColor = Widget.IconColor;
    }

    public string AutomationName => IsAvailable
        ? $"{DisplayName} folder{(IsExpanded ? ", expanded" : ", collapsed")}"
        : $"{DisplayName}, folder unavailable";
}
