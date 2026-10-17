using Hotline.Core.Settings;

namespace Hotline.Core.Windowing;

/// <summary>Which edges a resize drag moves (corners are two edges).</summary>
[Flags]
public enum ResizeEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

/// <summary>
/// Where the panel's bar sits and which way its chat grows (spec: docs/superpowers/specs/2026-10-16-panel-placement-design.md).
/// All rectangles are physical pixels in virtual-screen coordinates; sizes in settings are DIPs.
/// </summary>
public static class PanelPlacement
{
    /// <summary>
    /// The bar's home rectangle: DIP size scaled for the monitor (and never larger than its work area), placed at the
    /// anchor's fractions of the free space (0 = left/top edge, 1 = right/bottom edge). The default anchor (0.5, 0.8)
    /// is exactly <see cref="PopupGeometry.Place"/>.
    /// </summary>
    public static RectI Home(RectI work, int widthDip, int barHeightDip, double scale, PanelAnchor anchor)
    {
        var w = Math.Min((int)Math.Round(widthDip * scale), work.Width);
        var h = Math.Min((int)Math.Round(barHeightDip * scale), work.Height);
        var x = Math.Clamp(anchor.X, 0.0, 1.0);
        var y = Math.Clamp(anchor.Y, 0.0, 1.0);
        return new RectI(
            work.X + (int)Math.Floor((work.Width - w) * x + 1e-9),
            work.Y + (int)Math.Round((work.Height - h) * y),
            w, h);
    }

    /// <summary>The anchor that puts the bar back where it was dropped (Save as default). The monitor is the caller's.</summary>
    public static PanelAnchor AnchorFor(RectI bar, RectI work)
    {
        static double Fraction(int offset, int free) => free <= 0 ? 0.5 : Math.Clamp((double)offset / free, 0.0, 1.0);
        return new PanelAnchor
        {
            X = Fraction(bar.X - work.X, work.Width - bar.Width),
            Y = Fraction(bar.Y - work.Y, work.Height - bar.Height),
        };
    }

    /// <summary>
    /// Whether the chat grows upward from the bar. Locked directions are kept. Auto: up whenever the chat
    /// (<paramref name="chatPx"/> at its full height) fits above the bar, else down when it fits below, else the side
    /// with more room (up on a tie).
    /// </summary>
    public static bool GrowsUp(GrowDirection direction, RectI bar, RectI work, int chatPx)
    {
        if (direction == GrowDirection.Up) return true;
        if (direction == GrowDirection.Down) return false;
        var above = bar.Y - work.Y;
        var below = work.Y + work.Height - (bar.Y + bar.Height);
        if (above >= chatPx) return true;
        if (below >= chatPx) return false;
        return above >= below;
    }

    /// <summary>The rectangle pulled fully inside the work area (shrunk first when it's larger).</summary>
    public static RectI Clamp(RectI r, RectI work)
    {
        var w = Math.Min(r.Width, work.Width);
        var h = Math.Min(r.Height, work.Height);
        return new RectI(
            Math.Clamp(r.X, work.X, work.X + work.Width - w),
            Math.Clamp(r.Y, work.Y, work.Y + work.Height - h),
            w, h);
    }

    /// <summary>
    /// A resize drag: the given edges move by the pointer's travel, the others stay; never smaller than the minimum
    /// size and never past the work area.
    /// </summary>
    public static RectI Resize(RectI start, ResizeEdge edges, int dx, int dy, int minWidth, int minHeight, RectI work)
    {
        start = Clamp(start, work); // a window dragged partly off-screen starts from its on-screen part
        int left = start.X, top = start.Y, right = start.X + start.Width, bottom = start.Y + start.Height;
        int workRight = work.X + work.Width, workBottom = work.Y + work.Height;
        if (edges.HasFlag(ResizeEdge.Left)) left = Math.Clamp(left + dx, work.X, Math.Max(work.X, right - minWidth));
        if (edges.HasFlag(ResizeEdge.Right)) right = Math.Clamp(right + dx, Math.Min(workRight, left + minWidth), workRight);
        if (edges.HasFlag(ResizeEdge.Top)) top = Math.Clamp(top + dy, work.Y, Math.Max(work.Y, bottom - minHeight));
        if (edges.HasFlag(ResizeEdge.Bottom)) bottom = Math.Clamp(bottom + dy, Math.Min(workBottom, top + minHeight), workBottom);
        return new RectI(left, top, right - left, bottom - top);
    }

    /// <summary>A rectangle moved to a monitor with another scale keeps its DIP size (its origin stays).</summary>
    public static RectI Rescale(RectI r, double fromScale, double toScale)
    {
        var f = fromScale <= 0 ? 1.0 : toScale / fromScale;
        return r with { Width = (int)Math.Round(r.Width * f), Height = (int)Math.Round(r.Height * f) };
    }
}
