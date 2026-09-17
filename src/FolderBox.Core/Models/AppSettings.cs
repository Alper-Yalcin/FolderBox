namespace FolderBox.Core.Models;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>User settings. Kept intentionally small.</summary>
public sealed class AppSettings
{
    // General
    public bool StartWithWindows { get; set; } = true;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool AlignToGrid { get; set; } = true;
    /// <summary>Snap to Windows' own desktop icon grid (tiles become the size of a desktop icon cell).</summary>
    public bool UseDesktopIconGrid { get; set; } = true;
    public bool ClosePanelOnOutsideClick { get; set; } = true;
    public bool ShowItemCount { get; set; } = true;
    public bool LockAllWidgets { get; set; }
    /// <summary>Show "FolderBox" in Explorer's right-click "New" menu.</summary>
    public bool AddToNewMenu { get; set; } = true;
    /// <summary>Set after the one-time Desktop launcher shortcut was created.</summary>
    public bool DesktopShortcutCreated { get; set; }

    // Layout (logical pixels)
    public int GridWidth { get; set; } = 110;
    public int GridHeight { get; set; } = 110;
    public int PanelWidth { get; set; } = 440;
    public int PanelMaxHeight { get; set; } = 600;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Clamps values to sane ranges so a hand-edited file cannot break layout.</summary>
    public void Normalize()
    {
        GridWidth = Math.Clamp(GridWidth, 80, 200);
        GridHeight = Math.Clamp(GridHeight, 80, 200);
        PanelWidth = Math.Clamp(PanelWidth, 320, 720);
        PanelMaxHeight = Math.Clamp(PanelMaxHeight, 300, 900);
    }
}
