using FolderBox.Core.Models;

namespace FolderBox.Core.Services;

public enum PanelDirection
{
    Right,
    Left,
    Below,
    Above,
}

/// <summary>Result of a placement: where the panel goes and which side of the tile it hangs off.</summary>
public readonly record struct PanelPlacementResult(PixelRect Rect, PanelDirection Direction, int NotchOffset);

/// <summary>
/// Chooses the side of a tile on which an expanded panel opens so that the panel never leaves the
/// monitor's work area. Preference order: right, left, below, above; if none fits fully the side with
/// the most room is used and the panel is clamped.
/// </summary>
public static class PanelPlacement
{
    public static PanelPlacementResult Place(PixelRect tile, int panelWidth, int panelHeight, PixelRect workArea, int gap, PanelDirection? forcedDirection = null)
    {
        panelWidth = Math.Min(panelWidth, Math.Max(1, workArea.Width));
        panelHeight = Math.Min(panelHeight, Math.Max(1, workArea.Height));

        var spaceRight = workArea.Right - tile.Right - gap;
        var spaceLeft = tile.Left - workArea.Left - gap;
        var spaceBelow = workArea.Bottom - tile.Bottom - gap;
        var spaceAbove = tile.Top - workArea.Top - gap;

        PanelDirection direction;
        if (forcedDirection is { } forced) direction = forced;
        else if (spaceRight >= panelWidth) direction = PanelDirection.Right;
        else if (spaceLeft >= panelWidth) direction = PanelDirection.Left;
        else if (spaceBelow >= panelHeight) direction = PanelDirection.Below;
        else if (spaceAbove >= panelHeight) direction = PanelDirection.Above;
        else
        {
            // Nothing fits perfectly: take the side with the most room (horizontal sides first on ties).
            var best = new (PanelDirection Dir, int Space)[]
            {
                (PanelDirection.Right, spaceRight),
                (PanelDirection.Left, spaceLeft),
                (PanelDirection.Below, spaceBelow),
                (PanelDirection.Above, spaceAbove),
            }.OrderByDescending(t => t.Space).First();
            direction = best.Dir;
        }

        int x, y;
        switch (direction)
        {
            case PanelDirection.Right:
                x = tile.Right + gap;
                y = tile.Top;
                break;
            case PanelDirection.Left:
                x = tile.Left - gap - panelWidth;
                y = tile.Top;
                break;
            case PanelDirection.Below:
                x = tile.Left + tile.Width / 2 - panelWidth / 2;
                y = tile.Bottom + gap;
                break;
            default:
                x = tile.Left + tile.Width / 2 - panelWidth / 2;
                y = tile.Top - gap - panelHeight;
                break;
        }

        x = Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - panelWidth));
        y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - panelHeight));
        var rect = new PixelRect(x, y, panelWidth, panelHeight);

        // Where along the panel's tile-facing edge the connector should sit (centre of the tile).
        var notch = direction is PanelDirection.Right or PanelDirection.Left
            ? tile.Top + tile.Height / 2 - rect.Top
            : tile.Left + tile.Width / 2 - rect.Left;
        var maxNotch = direction is PanelDirection.Right or PanelDirection.Left ? rect.Height : rect.Width;
        notch = Math.Clamp(notch, 12, Math.Max(12, maxNotch - 12));

        return new PanelPlacementResult(rect, direction, notch);
    }
}
