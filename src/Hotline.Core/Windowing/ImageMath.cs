namespace Hotline.Core.Windowing;

public static class ImageMath
{
    /// <summary>Scales (w,h) so the longest edge is at most <paramref name="maxPx"/>; never upscales, never returns 0.</summary>
    public static (int Width, int Height) FitWithin(int width, int height, int maxPx)
    {
        var longest = Math.Max(width, height);
        if (longest <= maxPx) return (width, height);
        var scale = (double)maxPx / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}
