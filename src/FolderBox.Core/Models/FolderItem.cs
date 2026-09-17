namespace FolderBox.Core.Models;

/// <summary>An entry inside a real folder (file or sub-folder).</summary>
public sealed record FolderItem(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTime ModifiedUtc,
    string Extension)
{
    public bool IsFile => !IsDirectory;
}
