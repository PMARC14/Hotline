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
}
