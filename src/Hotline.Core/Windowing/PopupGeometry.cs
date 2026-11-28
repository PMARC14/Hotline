namespace Hotline.Core.Windowing;

/// <summary>Rectangle in physical pixels (virtual-screen coordinates; may be negative).</summary>
public readonly record struct RectI(int X, int Y, int Width, int Height);

public static class PopupGeometry
{
    /// <summary>
    /// Grows the popup upward from its bar: keeps the bar's bottom edge, height = content clamped to
    /// [bar height, maxPx], and never above the work area's top.
    /// </summary>
    public static RectI GrowUp(RectI bar, int contentPx, int maxPx, RectI workArea, Settings.GrowMode mode, bool conversation = true, bool manualSize = false)
        => GrowUp(bar, FullHeight(mode, contentPx, bar.Height, maxPx, conversation, manualSize), maxPx, workArea);

    /// <summary>
    /// The height to grow to: <see cref="Settings.GrowMode.Full"/> means the maximum, but only while a conversation is active and no
    /// size was set by hand (a held manual height wins); before the first message, or in a new chat, the panel is a bar that fits
    /// its content whatever the mode says. Anything else is the content's own height.
    /// </summary>
    public static int FullHeight(Settings.GrowMode mode, int contentPx, int barPx, int maxPx, bool conversation, bool manualSize)
        => mode == Settings.GrowMode.Full && conversation && !manualSize && contentPx > barPx ? maxPx : contentPx;

    public static RectI GrowUp(RectI bar, int contentPx, int maxPx, RectI workArea)
    {
        var bottom = bar.Y + bar.Height;
        var room = Math.Min(workArea.Height, bottom - workArea.Y);
        var h = Math.Clamp(contentPx, bar.Height, Math.Max(bar.Height, Math.Min(maxPx, room)));
        var y = Math.Max(workArea.Y, bottom - h);
        return bar with { Y = y, Height = h };
    }

    /// <summary>
    /// Grows the popup downward from its bar: keeps the bar's top edge, height = content clamped to
    /// [bar height, maxPx], and never below the work area's bottom.
    /// </summary>
    public static RectI GrowDown(RectI bar, int contentPx, int maxPx, RectI workArea, Settings.GrowMode mode, bool conversation = true, bool manualSize = false)
        => GrowDown(bar, FullHeight(mode, contentPx, bar.Height, maxPx, conversation, manualSize), maxPx, workArea);

    public static RectI GrowDown(RectI bar, int contentPx, int maxPx, RectI workArea)
    {
        var room = workArea.Y + workArea.Height - bar.Y;
        var h = Math.Clamp(contentPx, bar.Height, Math.Max(bar.Height, Math.Min(maxPx, room)));
        return bar with { Height = h };
    }

    /// <summary>
    /// The panel height for <paramref name="contentPx"/> given the height it has now: after the first message it
    /// expands to halfway between the bar and <paramref name="maxPx"/> at once (so a short answer never resizes it
    /// again), and only when the text outgrows that does it grow with the text, up to the maximum. It holds its height
    /// mid-chat. Without a <paramref name="conversation"/> (no message yet, or a new chat) it fits its content
    /// exactly: just the bar, a longer draft, a notice.
    /// </summary>
    public static int TwoStep(int previousPx, int contentPx, int floorPx, int maxPx, bool conversation)
    {
        if (!conversation || maxPx <= floorPx) return contentPx;
        var half = floorPx + (maxPx - floorPx) / 2;
        var size = Math.Max(contentPx, half);
        return Math.Min(maxPx, Math.Max(size, previousPx));
    }

    /// <summary>
    /// The rectangle widened to <paramref name="targetPx"/> around its horizontal centre, then slid back inside the work area (and no
    /// wider than it). A target that is not wider leaves it as it is.
    /// </summary>
    public static RectI GrowWidth(RectI rect, int targetPx, RectI work)
    {
        if (targetPx <= rect.Width) return rect;
        var w = Math.Min(targetPx, work.Width);
        var x = Math.Clamp(rect.X + (rect.Width - w) / 2, work.X, work.X + work.Width - w);
        return rect with { X = x, Width = w };
    }
}
