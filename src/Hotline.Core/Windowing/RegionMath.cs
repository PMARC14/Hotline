namespace Hotline.Core.Windowing;

/// <summary>Region capture: turn a mouse drag into a pixel rectangle and cut it out of a screen grab.</summary>
public static class RegionMath
{
    /// <summary>The dragged rectangle (any direction) in physical pixels, clamped to the screen.</summary>
    public static RectI Selection(double x1, double y1, double x2, double y2, double scale, int screenWidth, int screenHeight)
    {
        int Px(double dip, int max) => Math.Clamp((int)Math.Round(dip * scale), 0, max);
        var left = Px(Math.Min(x1, x2), screenWidth);
        var top = Px(Math.Min(y1, y2), screenHeight);
        var right = Px(Math.Max(x1, x2), screenWidth);
        var bottom = Px(Math.Max(y1, y2), screenHeight);
        return new RectI(left, top, right - left, bottom - top);
    }

    /// <summary>Anything smaller than 8×8 pixels is treated as a click, not a selection.</summary>
    public static bool IsUsable(RectI r) => r.Width >= 8 && r.Height >= 8;

    /// <summary>Copies the selection out of a top-down BGRA image.</summary>
    public static byte[] CropBgra(byte[] bgra, int width, int height, RectI r)
    {
        if (r.X < 0 || r.Y < 0 || r.Width <= 0 || r.Height <= 0 || r.X + r.Width > width || r.Y + r.Height > height)
            throw new ArgumentOutOfRangeException(nameof(r), $"{r} is outside the {width}x{height} image");
        var result = new byte[r.Width * r.Height * 4];
        for (var row = 0; row < r.Height; row++)
            Buffer.BlockCopy(bgra, ((r.Y + row) * width + r.X) * 4, result, row * r.Width * 4, r.Width * 4);
        return result;
    }
}
