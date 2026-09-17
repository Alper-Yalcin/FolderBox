namespace FolderBox.Core.Models;

/// <summary>Versioned on-disk document for widgets.</summary>
public sealed class FoldersDocument
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public List<FolderWidget> Folders { get; set; } = new();
}

/// <summary>Versioned on-disk document for settings.</summary>
public sealed class SettingsDocument
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public AppSettings Settings { get; set; } = new();
}
