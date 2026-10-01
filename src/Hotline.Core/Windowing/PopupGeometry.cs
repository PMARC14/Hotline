namespace Hotline.Core.Windowing;

/// <summary>Rectangle in physical pixels (virtual-screen coordinates; may be negative).</summary>
public readonly record struct RectI(int X, int Y, int Width, int Height);

public static class PopupGeometry
{
    /// <summary>Centers a DIP-sized popup in a monitor work area, scaled by that monitor's DPI and clamped to fit.</summary>
    public static RectI CenterIn(RectI workArea, int width, int height, double scale)
    {
        var w = Math.Min((int)Math.Round(width * scale), workArea.Width);
        var h = Math.Min((int)Math.Round(height * scale), workArea.Height);
        return new RectI(workArea.X + (workArea.Width - w) / 2, workArea.Y + (workArea.Height - h) / 2, w, h);
    }
}
