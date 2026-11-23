namespace Hotline.Core.Windowing;

/// <summary>Conversions between real screen pixels and scaled pixels.</summary>
public static class ScreenScale
{
    /// <summary>Windows' interface size (the scaled units) of a screen of <paramref name="widthPx"/> × <paramref name="heightPx"/> real pixels at <paramref name="scale"/> (1 = 100%).</summary>
    public static (int Width, int Height) Interface(int widthPx, int heightPx, double scale)
    {
        scale = scale is > 0.5 and < 8 ? scale : 1;
        return ((int)Math.Round(widthPx / scale), (int)Math.Round(heightPx / scale));
    }

}
