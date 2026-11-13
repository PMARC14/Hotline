namespace Hotline.Core.Windowing;

/// <summary>Calculates window placement and sizing constrained to a work area.</summary>
public static class WindowSizing
{
    /// <summary>
    /// Calculates a window rectangle of the desired DIP size scaled for DPI, clamped so it never
    /// exceeds the work area, and centered inside the work area.
    /// </summary>
    public static RectI Centered(RectI work, int widthDip, int heightDip, double scale)
    {
        var s = scale <= 0 ? 1.0 : scale;
        var desiredW = Math.Max(0, (int)Math.Round(widthDip * s));
        var desiredH = Math.Max(0, (int)Math.Round(heightDip * s));
        var clamped = PanelPlacement.Clamp(new RectI(work.X, work.Y, desiredW, desiredH), work);
        var x = work.X + (work.Width - clamped.Width) / 2;
        var y = work.Y + (work.Height - clamped.Height) / 2;
        return PanelPlacement.Clamp(new RectI(x, y, clamped.Width, clamped.Height), work);
    }
}
