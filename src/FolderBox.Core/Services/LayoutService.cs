using FolderBox.Core.Models;

namespace FolderBox.Core.Services;

/// <summary>
/// Pure layout math for widget tiles: grid snapping, occupancy resolution and clamping.
/// All coordinates are logical pixels relative to the monitor's top-left corner.
/// The grid itself comes from <see cref="GridProvider"/>: by default FolderBox's own grid from the
/// settings, in the app Windows' desktop icon grid so tiles line up with ordinary desktop icons.
/// </summary>
public sealed class LayoutService
{
    /// <summary>Logical size of a tile when FolderBox's own grid is used.</summary>
    public const double DefaultTileWidth = 100;
    public const double DefaultTileHeight = 108;

    public readonly record struct Cell(int Col, int Row);

    /// <summary>Areas (e.g. Windows' own desktop icons) a tile must never overlap, per monitor.</summary>
    public delegate IReadOnlyList<LogicalRect> BlockedAreaProvider(MonitorInfo monitor);

    /// <summary>Supplies the lattice for a monitor.</summary>
    public delegate GridSpec GridSpecProvider(MonitorInfo monitor, AppSettings settings);

    private static readonly BlockedAreaProvider NoBlockedAreas = _ => Array.Empty<LogicalRect>();

    public GridSpecProvider GridProvider { get; set; } = DefaultGrid;

    /// <summary>FolderBox's own grid: settings-sized cells starting at the work-area corner.</summary>
    public static GridSpec DefaultGrid(MonitorInfo monitor, AppSettings settings)
    {
        var (left, top, _, _) = monitor.LogicalWorkArea;
        return new GridSpec(settings.GridWidth, settings.GridHeight, left, top, DefaultTileWidth, DefaultTileHeight);
    }

    public GridSpec GridFor(MonitorInfo monitor, AppSettings settings) => GridProvider(monitor, settings);

    public static LogicalRect TileRect(LogicalPoint topLeft, GridSpec grid) =>
        new(topLeft.X, topLeft.Y, topLeft.X + grid.TileWidth, topLeft.Y + grid.TileHeight);

    /// <summary>Top-left position of the tile that sits in the given grid cell (centred in the cell).</summary>
    public LogicalPoint CellToPosition(Cell cell, MonitorInfo monitor, AppSettings settings)
    {
        var g = GridFor(monitor, settings);
        var padX = Math.Max(0, (g.CellWidth - g.TileWidth) / 2);
        var padY = Math.Max(0, (g.CellHeight - g.TileHeight) / 2);
        return new LogicalPoint(g.OriginX + cell.Col * g.CellWidth + padX, g.OriginY + cell.Row * g.CellHeight + padY);
    }

    /// <summary>Grid cell whose area contains the centre of a tile placed at <paramref name="position"/>.</summary>
    public Cell PositionToCell(LogicalPoint position, MonitorInfo monitor, AppSettings settings)
    {
        var g = GridFor(monitor, settings);
        var cx = position.X + g.TileWidth / 2 - g.OriginX;
        var cy = position.Y + g.TileHeight / 2 - g.OriginY;
        var (cols, rows) = GridDimensions(monitor, settings);
        var col = (int)Math.Floor(cx / g.CellWidth);
        var row = (int)Math.Floor(cy / g.CellHeight);
        return new Cell(Math.Clamp(col, 0, Math.Max(0, cols - 1)), Math.Clamp(row, 0, Math.Max(0, rows - 1)));
    }

    public (int Cols, int Rows) GridDimensions(MonitorInfo monitor, AppSettings settings)
    {
        var g = GridFor(monitor, settings);
        var (_, _, right, bottom) = monitor.LogicalWorkArea;
        var cols = Math.Max(1, (int)Math.Floor((right - g.OriginX) / g.CellWidth));
        var rows = Math.Max(1, (int)Math.Floor((bottom - g.OriginY) / g.CellHeight));
        return (cols, rows);
    }

    /// <summary>Keeps a tile fully inside the monitor's work area.</summary>
    public LogicalPoint ClampToWorkArea(LogicalPoint p, MonitorInfo monitor, AppSettings settings)
    {
        var g = GridFor(monitor, settings);
        var (left, top, right, bottom) = monitor.LogicalWorkArea;
        var maxX = Math.Max(left, right - g.TileWidth);
        var maxY = Math.Max(top, bottom - g.TileHeight);
        return new LogicalPoint(Math.Clamp(p.X, left, maxX), Math.Clamp(p.Y, top, maxY));
    }

    /// <summary>True when a tile placed in the cell would overlap a blocked area (desktop icon).</summary>
    public bool IsCellBlocked(Cell cell, MonitorInfo monitor, AppSettings settings, IReadOnlyList<LogicalRect> blocked)
    {
        if (blocked.Count == 0) return false;
        return Overlaps(TileRect(CellToPosition(cell, monitor, settings), GridFor(monitor, settings)), blocked);
    }

    private static bool Overlaps(LogicalRect rect, IReadOnlyList<LogicalRect> blocked)
    {
        foreach (var b in blocked)
            if (rect.IntersectsWith(b)) return true;
        return false;
    }

    /// <summary>
    /// Resolves where a tile ends up after being dropped at <paramref name="desired"/>.
    /// In grid mode the nearest free cell (deterministic ring search) is chosen; otherwise the
    /// position is only clamped — unless it would cover a desktop icon or another tile.
    /// </summary>
    public LogicalPoint ResolveDropPosition(
        LogicalPoint desired,
        MonitorInfo monitor,
        IEnumerable<FolderWidget> otherWidgetsOnMonitor,
        AppSettings settings,
        BlockedAreaProvider? blockedAreas = null)
    {
        var g = GridFor(monitor, settings);
        var blocked = (blockedAreas ?? NoBlockedAreas)(monitor);
        var others = otherWidgetsOnMonitor.Where(w => w.IsVisible).ToList();
        var occupied = new HashSet<Cell>();
        foreach (var w in others)
            occupied.Add(PositionToCell(new LogicalPoint(w.X, w.Y), monitor, settings));

        if (!settings.AlignToGrid)
        {
            var free = ClampToWorkArea(desired, monitor, settings);
            var tileRect = TileRect(free, g);
            var coversTile = others.Any(w => tileRect.IntersectsWith(TileRect(new LogicalPoint(w.X, w.Y), g)));
            if (!Overlaps(tileRect, blocked) && !coversTile) return free;
        }

        var wanted = PositionToCell(desired, monitor, settings);
        var cell = FindNearestFreeCell(wanted, occupied, monitor, settings, blocked);
        return ClampToWorkArea(CellToPosition(cell, monitor, settings), monitor, settings);
    }

    /// <summary>
    /// Deterministic search: expanding square rings around the wanted cell, candidates in each ring
    /// ordered by Euclidean distance, then row, then column.
    /// </summary>
    public Cell FindNearestFreeCell(Cell wanted, ISet<Cell> occupied, MonitorInfo monitor, AppSettings settings, IReadOnlyList<LogicalRect>? blocked = null)
    {
        blocked ??= Array.Empty<LogicalRect>();
        var (cols, rows) = GridDimensions(monitor, settings);
        wanted = new Cell(Math.Clamp(wanted.Col, 0, cols - 1), Math.Clamp(wanted.Row, 0, rows - 1));
        if (!occupied.Contains(wanted) && !IsCellBlocked(wanted, monitor, settings, blocked)) return wanted;

        var maxRing = Math.Max(cols, rows);
        for (int ring = 1; ring <= maxRing; ring++)
        {
            var candidates = new List<Cell>();
            for (int dr = -ring; dr <= ring; dr++)
            {
                for (int dc = -ring; dc <= ring; dc++)
                {
                    if (Math.Max(Math.Abs(dr), Math.Abs(dc)) != ring) continue;
                    var c = new Cell(wanted.Col + dc, wanted.Row + dr);
                    if (c.Col < 0 || c.Row < 0 || c.Col >= cols || c.Row >= rows) continue;
                    if (occupied.Contains(c) || IsCellBlocked(c, monitor, settings, blocked)) continue;
                    candidates.Add(c);
                }
            }
            if (candidates.Count == 0) continue;
            candidates.Sort((a, b) =>
            {
                double da = Dist(a, wanted), db = Dist(b, wanted);
                var cmp = da.CompareTo(db);
                if (cmp != 0) return cmp;
                cmp = a.Row.CompareTo(b.Row);
                return cmp != 0 ? cmp : a.Col.CompareTo(b.Col);
            });
            return candidates[0];
        }
        // Grid completely full: fall back to the wanted cell (overlap is better than losing the widget).
        return wanted;

        static double Dist(Cell a, Cell b)
        {
            double dx = a.Col - b.Col, dy = a.Row - b.Row;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// Assigns positions to a set of widgets at startup: widgets on missing monitors are moved to the
    /// primary monitor, clamped, and (in grid mode) de-duplicated so no two share a cell or a desktop icon.
    /// Returns true when any widget was changed.
    /// </summary>
    public bool NormalizeLayout(IList<FolderWidget> widgets, IReadOnlyList<MonitorInfo> monitors, AppSettings settings, BlockedAreaProvider? blockedAreas = null)
    {
        if (monitors.Count == 0) return false;
        blockedAreas ??= NoBlockedAreas;
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var changed = false;
        var occupiedByMonitor = new Dictionary<string, HashSet<Cell>>(StringComparer.OrdinalIgnoreCase);

        foreach (var w in widgets)
        {
            var monitor = monitors.FirstOrDefault(m => string.Equals(m.Id, w.MonitorId, StringComparison.OrdinalIgnoreCase));
            if (monitor is null)
            {
                monitor = primary;
                w.MonitorId = primary.Id;
                changed = true;
            }

            var g = GridFor(monitor, settings);
            var pos = new LogicalPoint(w.X, w.Y);
            var blocked = blockedAreas(monitor);
            if (!occupiedByMonitor.TryGetValue(monitor.Id, out var occupied))
                occupiedByMonitor[monitor.Id] = occupied = new HashSet<Cell>();
            LogicalPoint resolved;
            if (settings.AlignToGrid || Overlaps(TileRect(ClampToWorkArea(pos, monitor, settings), g), blocked))
            {
                var cell = FindNearestFreeCell(PositionToCell(pos, monitor, settings), occupied, monitor, settings, blocked);
                occupied.Add(cell);
                resolved = ClampToWorkArea(CellToPosition(cell, monitor, settings), monitor, settings);
            }
            else
            {
                resolved = ClampToWorkArea(pos, monitor, settings);
            }

            if (Math.Abs(resolved.X - w.X) > 0.5 || Math.Abs(resolved.Y - w.Y) > 0.5)
            {
                w.X = resolved.X;
                w.Y = resolved.Y;
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Suggests a position for a newly added widget: first free cell scanning columns top-to-bottom.</summary>
    public LogicalPoint SuggestNewPosition(IEnumerable<FolderWidget> existingOnMonitor, MonitorInfo monitor, AppSettings settings, BlockedAreaProvider? blockedAreas = null)
    {
        var blocked = (blockedAreas ?? NoBlockedAreas)(monitor);
        var occupied = new HashSet<Cell>();
        foreach (var w in existingOnMonitor)
            occupied.Add(PositionToCell(new LogicalPoint(w.X, w.Y), monitor, settings));

        var (cols, rows) = GridDimensions(monitor, settings);
        // Windows fills the desktop column by column from the top-left; mirror that.
        for (int col = 0; col < cols; col++)
        {
            for (int row = 0; row < rows; row++)
            {
                var c = new Cell(col, row);
                if (!occupied.Contains(c) && !IsCellBlocked(c, monitor, settings, blocked))
                    return ClampToWorkArea(CellToPosition(c, monitor, settings), monitor, settings);
            }
        }
        return ClampToWorkArea(CellToPosition(new Cell(0, 0), monitor, settings), monitor, settings);
    }
}
