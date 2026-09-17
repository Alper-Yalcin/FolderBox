namespace FolderBox.Core.Utilities;

/// <summary>
/// Explorer-like ordering: digit runs compare numerically ("file2" before "file10"),
/// everything else compares case-insensitively. Pure managed so it is thread-agnostic and testable.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            char cx = x[ix], cy = y[iy];
            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                int sx = ix, sy = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;
                var nx = x.AsSpan(sx, ix - sx).TrimStart('0');
                var ny = y.AsSpan(sy, iy - sy).TrimStart('0');
                if (nx.Length != ny.Length) return nx.Length.CompareTo(ny.Length);
                var c = nx.SequenceCompareTo(ny);
                if (c != 0) return c;
                var lenDiff = (ix - sx).CompareTo(iy - sy);
                if (lenDiff != 0) return lenDiff;
                continue;
            }

            var cc = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
            if (cc != 0) return cc;
            ix++;
            iy++;
        }
        return (x.Length - ix).CompareTo(y.Length - iy);
    }
}
