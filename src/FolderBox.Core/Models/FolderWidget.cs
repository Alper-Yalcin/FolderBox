namespace FolderBox.Core.Models;

/// <summary>
/// A desktop widget bound to a real Windows folder. Coordinates are logical (DIP) pixels
/// relative to the top-left corner of the monitor identified by <see cref="MonitorId"/>.
/// </summary>
public sealed class FolderWidget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public string MonitorId { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public bool IsVisible { get; set; } = true;
    /// <summary>Icon design name (see the app's IconStyles catalog). Empty = default.</summary>
    public string IconStyle { get; set; } = string.Empty;
    /// <summary>Icon colour name from the catalog ("Accent", "Blue", ...) or a #RRGGBB value. Empty = accent.</summary>
    public string IconColor { get; set; } = string.Empty;

    public FolderWidget Clone() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        FolderPath = FolderPath,
        X = X,
        Y = Y,
        MonitorId = MonitorId,
        IsLocked = IsLocked,
        IsVisible = IsVisible,
        IconStyle = IconStyle,
        IconColor = IconColor,
    };
}
