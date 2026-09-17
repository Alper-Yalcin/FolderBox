using FolderBox.Core.Logging;
using FolderBox.Core.Models;
using FolderBox.Core.Utilities;

namespace FolderBox.Core.Services;

public enum FolderStatus
{
    Available,
    NotFound,
    AccessDenied,
    Error,
}

public sealed record FolderListing(FolderStatus Status, IReadOnlyList<FolderItem> Items, string? ErrorMessage = null);

/// <summary>Reads real folder contents on a background thread. Never throws to the caller.</summary>
public static class FolderService
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    public static FolderStatus GetStatus(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return FolderStatus.NotFound;
            if (!Directory.Exists(path)) return FolderStatus.NotFound;
            // Probe access cheaply.
            using var e = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            e.MoveNext();
            return FolderStatus.Available;
        }
        catch (UnauthorizedAccessException) { return FolderStatus.AccessDenied; }
        catch (IOException) { return FolderStatus.Error; }
        catch { return FolderStatus.Error; }
    }

    public static Task<FolderListing> ListAsync(string path, CancellationToken ct) =>
        Task.Run(() => List(path, ct), ct);

    public static FolderListing List(string path, CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(path))
                return new FolderListing(FolderStatus.NotFound, Array.Empty<FolderItem>(), "Folder not found");

            var dirs = new List<FolderItem>();
            var files = new List<FolderItem>();
            var dir = new DirectoryInfo(path);
            foreach (var info in dir.EnumerateFileSystemInfos("*", Options))
            {
                ct.ThrowIfCancellationRequested();
                var isDir = (info.Attributes & FileAttributes.Directory) != 0;
                long size = 0;
                if (!isDir && info is FileInfo fi)
                {
                    try { size = fi.Length; } catch { size = 0; }
                }
                DateTime modified;
                try { modified = info.LastWriteTimeUtc; } catch { modified = DateTime.MinValue; }
                var item = new FolderItem(info.Name, info.FullName, isDir, size, modified, isDir ? string.Empty : info.Extension);
                (isDir ? dirs : files).Add(item);
            }

            dirs.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.Name, b.Name));
            files.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.Name, b.Name));
            var all = new List<FolderItem>(dirs.Count + files.Count);
            all.AddRange(dirs);
            all.AddRange(files);
            return new FolderListing(FolderStatus.Available, all);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warn($"Access denied listing {path}: {ex.Message}");
            return new FolderListing(FolderStatus.AccessDenied, Array.Empty<FolderItem>(), "Access denied");
        }
        catch (DirectoryNotFoundException)
        {
            return new FolderListing(FolderStatus.NotFound, Array.Empty<FolderItem>(), "Folder not found");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to list {path}", ex);
            return new FolderListing(FolderStatus.Error, Array.Empty<FolderItem>(), ex.Message);
        }
    }

    /// <summary>Counts visible entries, capped so huge folders stay cheap. Returns -1 when unavailable.</summary>
    public static Task<int> CountAsync(string path, int cap, CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            if (!Directory.Exists(path)) return -1;
            var count = 0;
            foreach (var _ in Directory.EnumerateFileSystemEntries(path, "*", Options))
            {
                ct.ThrowIfCancellationRequested();
                if (++count >= cap) break;
            }
            return count;
        }
        catch (OperationCanceledException) { throw; }
        catch { return -1; }
    }, ct);
}
