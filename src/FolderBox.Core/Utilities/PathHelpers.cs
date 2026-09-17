namespace FolderBox.Core.Utilities;

public static class PathHelpers
{
    /// <summary>Returns the last path segment, handling drive roots (e.g. "D:\") gracefully.</summary>
    public static string GetDisplayNameForFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 2 && trimmed[1] == ':')
            return trimmed.ToUpperInvariant() + Path.DirectorySeparatorChar;
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    public static string NormalizeFolderPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            var full = Path.GetFullPath(path);
            // Keep the root form "D:\" intact, otherwise trim trailing separators.
            if (full.Length > 3) full = full.TrimEnd(Path.DirectorySeparatorChar);
            return full;
        }
        catch
        {
            return path;
        }
    }

    public static bool IsSameVolume(string pathA, string pathB)
    {
        try
        {
            var rootA = Path.GetPathRoot(Path.GetFullPath(pathA));
            var rootB = Path.GetPathRoot(Path.GetFullPath(pathB));
            return string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsSubPathOf(string child, string parent)
    {
        var c = NormalizeFolderPath(child);
        var p = NormalizeFolderPath(parent);
        if (!p.EndsWith(Path.DirectorySeparatorChar)) p += Path.DirectorySeparatorChar;
        return (c + Path.DirectorySeparatorChar).StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }
}
