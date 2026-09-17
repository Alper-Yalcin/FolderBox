using FolderBox.Core.Models;
using FolderBox.Core.Services;
using Xunit;

namespace FolderBox.Core.Tests;

public class PanelPlacementTests
{
    private static readonly PixelRect Work = new(0, 0, 1920, 1032);
    private const int W = 380, H = 560, Gap = 6;

    [Fact]
    public void Opens_to_the_right_when_there_is_room()
    {
        var tile = new PixelRect(40, 40, 100, 104);
        var r = PanelPlacement.Place(tile, W, H, Work, Gap);
        Assert.Equal(PanelDirection.Right, r.Direction);
        Assert.Equal(tile.Right + Gap, r.Rect.Left);
        Assert.Equal(tile.Top, r.Rect.Top);
    }

    [Fact]
    public void Opens_to_the_left_near_the_right_edge()
    {
        var tile = new PixelRect(1800, 40, 100, 104);
        var r = PanelPlacement.Place(tile, W, H, Work, Gap);
        Assert.Equal(PanelDirection.Left, r.Direction);
        Assert.Equal(tile.Left - Gap - W, r.Rect.Left);
    }

    [Fact]
    public void Opens_below_when_no_horizontal_room()
    {
        var narrow = new PixelRect(0, 0, 500, 1000);
        var tile = new PixelRect(200, 40, 100, 104);
        var r = PanelPlacement.Place(tile, W, H, narrow, Gap);
        Assert.Equal(PanelDirection.Below, r.Direction);
        Assert.Equal(tile.Bottom + Gap, r.Rect.Top);
    }

    [Fact]
    public void Opens_above_when_only_room_is_above()
    {
        var narrow = new PixelRect(0, 0, 500, 1000);
        var tile = new PixelRect(200, 880, 100, 104);
        var r = PanelPlacement.Place(tile, W, H, narrow, Gap);
        Assert.Equal(PanelDirection.Above, r.Direction);
        Assert.Equal(tile.Top - Gap - H, r.Rect.Top);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1820, 0)]
    [InlineData(0, 928)]
    [InlineData(1820, 928)]
    [InlineData(900, 500)]
    public void Panel_never_leaves_the_work_area(int x, int y)
    {
        var tile = new PixelRect(x, y, 100, 104);
        var r = PanelPlacement.Place(tile, W, H, Work, Gap);
        Assert.True(r.Rect.Left >= Work.Left);
        Assert.True(r.Rect.Top >= Work.Top);
        Assert.True(r.Rect.Right <= Work.Right);
        Assert.True(r.Rect.Bottom <= Work.Bottom);
    }

    [Fact]
    public void Bottom_edge_tile_is_shifted_up_so_the_panel_fits()
    {
        var tile = new PixelRect(40, 900, 100, 104);
        var r = PanelPlacement.Place(tile, W, H, Work, Gap);
        Assert.Equal(PanelDirection.Right, r.Direction);
        Assert.Equal(Work.Bottom - H, r.Rect.Top);
    }

    [Fact]
    public void Forced_direction_is_respected_and_clamped()
    {
        var tile = new PixelRect(40, 40, 100, 104);
        var r = PanelPlacement.Place(tile, W, H, Work, Gap, PanelDirection.Above);
        Assert.Equal(PanelDirection.Above, r.Direction);
        Assert.Equal(Work.Top, r.Rect.Top);
    }

    [Fact]
    public void Tiny_work_area_still_produces_a_rect_inside()
    {
        var tiny = new PixelRect(0, 0, 300, 300);
        var tile = new PixelRect(100, 100, 100, 104);
        var r = PanelPlacement.Place(tile, W, H, tiny, Gap);
        Assert.True(r.Rect.Width <= tiny.Width && r.Rect.Height <= tiny.Height);
        Assert.True(r.Rect.Left >= 0 && r.Rect.Top >= 0);
    }
}
