using FolderBox.Core.Models;
using FolderBox.Core.Services;
using Xunit;

namespace FolderBox.Core.Tests;

public class LayoutServiceTests
{
    private static readonly MonitorInfo Monitor = new("\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1.0, true);
    private static readonly MonitorInfo Monitor150 = new("\\\\.\\DISPLAY2", new PixelRect(1920, 0, 2880, 1620), new PixelRect(1920, 0, 2880, 1560), 1.5, false);
    private static AppSettings Settings(bool grid = true) => new() { AlignToGrid = grid, GridWidth = 110, GridHeight = 110 };

    [Fact]
    public void Drop_snaps_to_nearest_cell_in_grid_mode()
    {
        var layout = new LayoutService();
        var pos = layout.ResolveDropPosition(new LogicalPoint(130, 20), Monitor, Array.Empty<FolderWidget>(), Settings());
        var expected = layout.CellToPosition(new LayoutService.Cell(1, 0), Monitor, Settings());
        Assert.Equal(expected, pos);
    }

    [Fact]
    public void Drop_only_clamps_when_grid_is_off()
    {
        var layout = new LayoutService();
        var pos = layout.ResolveDropPosition(new LogicalPoint(-50, 2000), Monitor, Array.Empty<FolderWidget>(), Settings(grid: false));
        Assert.Equal(0, pos.X);
        Assert.Equal(1032 - LayoutService.DefaultTileHeight, pos.Y);
    }

    [Fact]
    public void Occupied_cell_is_avoided_deterministically()
    {
        var layout = new LayoutService();
        var settings = Settings();
        var occupiedPos = layout.CellToPosition(new LayoutService.Cell(0, 0), Monitor, settings);
        var other = new FolderWidget { Id = "a", X = occupiedPos.X, Y = occupiedPos.Y, MonitorId = Monitor.Id };

        var first = layout.ResolveDropPosition(occupiedPos, Monitor, new[] { other }, settings);
        var second = layout.ResolveDropPosition(occupiedPos, Monitor, new[] { other }, settings);

        Assert.Equal(first, second);
        Assert.NotEqual(occupiedPos, first);
        // Ring 1 candidates at distance 1: (1,0) and (0,1); row wins the tie-break -> (1,0)... but ordering is by distance then row then col.
        var cell = layout.PositionToCell(first, Monitor, settings);
        Assert.Equal(new LayoutService.Cell(1, 0), cell);
    }

    [Fact]
    public void Hidden_widgets_do_not_occupy_cells()
    {
        var layout = new LayoutService();
        var settings = Settings();
        var pos = layout.CellToPosition(new LayoutService.Cell(0, 0), Monitor, settings);
        var hidden = new FolderWidget { Id = "h", X = pos.X, Y = pos.Y, MonitorId = Monitor.Id, IsVisible = false };
        var result = layout.ResolveDropPosition(pos, Monitor, new[] { hidden }, settings);
        Assert.Equal(pos, result);
    }

    [Fact]
    public void Normalize_moves_widgets_from_missing_monitor_to_primary_and_separates_them()
    {
        var layout = new LayoutService();
        var settings = Settings();
        var widgets = new List<FolderWidget>
        {
            new() { Id = "1", X = 5, Y = 3, MonitorId = "\\\\.\\GONE" },
            new() { Id = "2", X = 5, Y = 3, MonitorId = "\\\\.\\GONE" },
        };
        var changed = layout.NormalizeLayout(widgets, new[] { Monitor }, settings);
        Assert.True(changed);
        Assert.All(widgets, w => Assert.Equal(Monitor.Id, w.MonitorId));
        var c1 = layout.PositionToCell(new LogicalPoint(widgets[0].X, widgets[0].Y), Monitor, settings);
        var c2 = layout.PositionToCell(new LogicalPoint(widgets[1].X, widgets[1].Y), Monitor, settings);
        Assert.NotEqual(c1, c2);
    }

    [Fact]
    public void Suggested_position_fills_columns_top_to_bottom()
    {
        var layout = new LayoutService();
        var settings = Settings();
        var first = layout.SuggestNewPosition(Array.Empty<FolderWidget>(), Monitor, settings);
        var w1 = new FolderWidget { Id = "1", X = first.X, Y = first.Y, MonitorId = Monitor.Id };
        var second = layout.SuggestNewPosition(new[] { w1 }, Monitor, settings);
        Assert.Equal(new LayoutService.Cell(0, 0), layout.PositionToCell(first, Monitor, settings));
        Assert.Equal(new LayoutService.Cell(0, 1), layout.PositionToCell(second, Monitor, settings));
    }

    [Fact]
    public void Physical_and_logical_conversion_round_trips_on_scaled_monitor()
    {
        var logical = new LogicalPoint(100, 50);
        var physical = Monitor150.ToPhysical(logical);
        Assert.Equal(new PixelPoint(1920 + 150, 75), physical);
        var back = Monitor150.ToLogical(physical);
        Assert.Equal(logical.X, back.X, 3);
        Assert.Equal(logical.Y, back.Y, 3);
    }

    [Fact]
    public void Grid_dimensions_respect_work_area()
    {
        var layout = new LayoutService();
        var (cols, rows) = layout.GridDimensions(Monitor, Settings());
        Assert.Equal(17, cols); // 1920 / 110
        Assert.Equal(9, rows);  // 1032 / 110
    }
}

public class DesktopIconAvoidanceTests
{
    private static readonly MonitorInfo Monitor = new("DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1.0, true);
    private static AppSettings Settings(bool grid = true) => new() { AlignToGrid = grid, GridWidth = 110, GridHeight = 110 };

    // Windows desktop icons in the first column (75 x 91 cells) like a typical desktop.
    private static IReadOnlyList<LogicalRect> FirstColumnIcons(MonitorInfo _) =>
        Enumerable.Range(0, 6).Select(r => new LogicalRect(0, r * 91, 75, r * 91 + 91)).ToList();

    [Fact]
    public void Drop_on_desktop_icons_moves_to_nearest_cell_that_does_not_overlap()
    {
        var layout = new LayoutService();
        var pos = layout.ResolveDropPosition(new LogicalPoint(5, 1), Monitor, Array.Empty<FolderWidget>(), Settings(), FirstColumnIcons);
        var rect = LayoutService.TileRect(pos, layout.GridFor(Monitor, Settings()));
        Assert.True(rect.Left >= 75, $"tile at {pos} still overlaps the icon column");
        Assert.All(FirstColumnIcons(Monitor), icon => Assert.False(rect.IntersectsWith(icon)));
    }

    [Fact]
    public void Free_positioning_also_avoids_desktop_icons()
    {
        var layout = new LayoutService();
        var pos = layout.ResolveDropPosition(new LogicalPoint(10, 10), Monitor, Array.Empty<FolderWidget>(), Settings(grid: false), FirstColumnIcons);
        Assert.All(FirstColumnIcons(Monitor), icon => Assert.False(LayoutService.TileRect(pos, layout.GridFor(Monitor, Settings())).IntersectsWith(icon)));

        var freeSpot = layout.ResolveDropPosition(new LogicalPoint(600, 300), Monitor, Array.Empty<FolderWidget>(), Settings(grid: false), FirstColumnIcons);
        Assert.Equal(new LogicalPoint(600, 300), freeSpot);
    }

    [Fact]
    public void New_widget_suggestion_skips_blocked_cells()
    {
        var layout = new LayoutService();
        var pos = layout.SuggestNewPosition(Array.Empty<FolderWidget>(), Monitor, Settings(), FirstColumnIcons);
        Assert.All(FirstColumnIcons(Monitor), icon => Assert.False(LayoutService.TileRect(pos, layout.GridFor(Monitor, Settings())).IntersectsWith(icon)));
    }

    [Fact]
    public void Desktop_grid_places_tiles_in_free_icon_cells_next_to_icons()
    {
        // Windows grid: 75x91 cells; icons occupy column 0 rows 0..5. A tile dropped at column 0 must land in column 1, row 0.
        var layout = new LayoutService { GridProvider = (m, _) => new GridSpec(75, 91, 0, 0, 75, 91) };
        var pos = layout.ResolveDropPosition(new LogicalPoint(3, 2), Monitor, Array.Empty<FolderWidget>(), Settings(), FirstColumnIcons);
        Assert.Equal(new LogicalPoint(75, 0), pos);
        Assert.Equal(new LayoutService.Cell(1, 0), layout.PositionToCell(pos, Monitor, Settings()));
    }

    [Fact]
    public void Startup_normalisation_moves_widgets_off_desktop_icons()
    {
        var layout = new LayoutService();
        var widgets = new List<FolderWidget> { new() { Id = "1", X = 5, Y = 1, MonitorId = Monitor.Id } };
        var changed = layout.NormalizeLayout(widgets, new[] { Monitor }, Settings(), FirstColumnIcons);
        Assert.True(changed);
        var rect = LayoutService.TileRect(new LogicalPoint(widgets[0].X, widgets[0].Y), layout.GridFor(Monitor, Settings()));
        Assert.All(FirstColumnIcons(Monitor), icon => Assert.False(rect.IntersectsWith(icon)));
    }
}
