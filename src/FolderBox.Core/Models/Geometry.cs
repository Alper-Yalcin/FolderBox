namespace FolderBox.Core.Models;

/// <summary>Integer rectangle in physical screen pixels (virtual-screen coordinates).</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    public bool IntersectsWith(PixelRect other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
    public static PixelRect FromLTRB(int l, int t, int r, int b) => new(l, t, r - l, b - t);
    public int Area => Math.Max(0, Width) * Math.Max(0, Height);
}

public readonly record struct PixelPoint(int X, int Y);

/// <summary>Logical (DIP) point relative to a monitor origin.</summary>
public readonly record struct LogicalPoint(double X, double Y);

/// <summary>Logical (DIP) rectangle relative to a monitor origin.</summary>
public readonly record struct LogicalRect(double Left, double Top, double Right, double Bottom)
{
    public bool IntersectsWith(LogicalRect o) => Left < o.Right && o.Left < Right && Top < o.Bottom && o.Top < Bottom;
}

/// <summary>
/// The lattice tiles snap to on one monitor (logical, monitor-relative). Either Windows' own desktop
/// icon grid (cell = icon spacing, tile = cell) or FolderBox's configurable grid.
/// </summary>
public sealed record GridSpec(double CellWidth, double CellHeight, double OriginX, double OriginY, double TileWidth, double TileHeight);

/// <summary>Description of a display monitor.</summary>
public sealed record MonitorInfo(
    string Id,
    PixelRect Bounds,
    PixelRect WorkArea,
    double Scale,
    bool IsPrimary)
{
    public PixelPoint ToPhysical(LogicalPoint p) =>
        new(Bounds.Left + (int)Math.Round(p.X * Scale), Bounds.Top + (int)Math.Round(p.Y * Scale));

    public LogicalPoint ToLogical(PixelPoint p) =>
        new((p.X - Bounds.Left) / Scale, (p.Y - Bounds.Top) / Scale);

    /// <summary>Work area expressed in logical pixels relative to the monitor origin.</summary>
    public (double Left, double Top, double Right, double Bottom) LogicalWorkArea =>
        ((WorkArea.Left - Bounds.Left) / Scale,
         (WorkArea.Top - Bounds.Top) / Scale,
         (WorkArea.Right - Bounds.Left) / Scale,
         (WorkArea.Bottom - Bounds.Top) / Scale);
}
