namespace Hotline.Core.Windowing;

/// <summary>Rectangle in physical pixels (virtual-screen coordinates; may be negative).</summary>
public readonly record struct RectI(int X, int Y, int Width, int Height);

public static class PopupGeometry
{
    /// <summary>
    /// Places a DIP-sized popup in a monitor work area: scaled by that monitor's DPI, clamped to fit,
    /// centered horizontally, and at <paramref name="verticalPosition"/> of the free vertical space
    /// (0 = top, 0.5 = centered, 1 = bottom; clamped to that range).
    /// </summary>
    public static RectI Place(RectI workArea, int width, int height, double scale, double verticalPosition)
    {
        var w = Math.Min((int)Math.Round(width * scale), workArea.Width);
        var h = Math.Min((int)Math.Round(height * scale), workArea.Height);
        var v = Math.Clamp(verticalPosition, 0.0, 1.0);
        return new RectI(
            workArea.X + (workArea.Width - w) / 2,
            workArea.Y + (int)Math.Round((workArea.Height - h) * v),
            w, h);
    }

    /// <summary>
    /// Grows the popup upward from its bar: keeps the bar's bottom edge, height = content clamped to
    /// [bar height, maxPx], and never above the work area's top.
    /// </summary>
    public static RectI GrowUp(RectI bar, int contentPx, int maxPx, RectI workArea, Settings.GrowMode mode)
        => GrowUp(bar, mode == Settings.GrowMode.Full && contentPx > bar.Height ? maxPx : contentPx, maxPx, workArea);

    public static RectI GrowUp(RectI bar, int contentPx, int maxPx, RectI workArea)
    {
        var h = Math.Clamp(contentPx, bar.Height, Math.Max(bar.Height, Math.Min(maxPx, workArea.Height)));
        var bottom = bar.Y + bar.Height;
        var y = Math.Max(workArea.Y, bottom - h);
        return bar with { Y = y, Height = h };
    }

    /// <summary>
    /// The panel height for <paramref name="contentPx"/> given the height it has now, in two sizes: once a chat starts it
    /// jumps to halfway between the bar and <paramref name="maxPx"/>, and when the conversation outgrows that, to the
    /// maximum. Resizing a window that grows upward can't be smooth (Windows shows the old frame at the new top edge
    /// until the next one is drawn), so it resizes twice per chat instead of once per streamed line. It holds its height
    /// mid-chat; within <paramref name="slackPx"/> of <paramref name="floorPx"/> (the empty panel: just the message
    /// bar, e.g. a new chat) it fits exactly.
    /// </summary>
    public static int TwoStep(int previousPx, int contentPx, int floorPx, int maxPx, int slackPx)
    {
        if (contentPx <= floorPx + slackPx || maxPx <= floorPx) return contentPx;
        var half = floorPx + (maxPx - floorPx) / 2;
        var size = contentPx <= half ? half : maxPx;
        return Math.Min(maxPx, Math.Max(size, previousPx));
    }

    /// <summary>Panel width in DIPs: percent of the work area (converted from physical pixels), clamped to [min, max].</summary>
    public static int RelativeWidthDip(int workAreaWidthPx, double scale, double percent, int minDip, int maxDip)
        => Math.Clamp((int)Math.Round(workAreaWidthPx / scale * percent / 100.0), minDip, Math.Max(minDip, maxDip));
}
