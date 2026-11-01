using Hotline.Core.Text.Localization;

namespace Hotline.Core.Windowing;

/// <summary>The one short line Settings shows above the button rows: the screen's real pixels, Windows' scaling and the interface size that scaling gives.</summary>
public static class ScreenScale
{
    /// <summary>Windows' interface size (the scaled units) of a screen of <paramref name="widthPx"/> × <paramref name="heightPx"/> real pixels at <paramref name="scale"/> (1 = 100%).</summary>
    public static (int Width, int Height) Interface(int widthPx, int heightPx, double scale)
    {
        scale = scale is > 0.5 and < 8 ? scale : 1;
        return ((int)Math.Round(widthPx / scale), (int)Math.Round(heightPx / scale));
    }

    /// <summary>"Screen 2880 × 1800 px at 200% scaling: Windows draws the interface at 1440 × 900".</summary>
    public static string Line(int widthPx, int heightPx, double scale)
    {
        scale = scale is > 0.5 and < 8 ? scale : 1;
        var percent = (int)Math.Round(scale * 100);
        if (percent == 100) return L.F(Strings.Bar_ScreenLine100, widthPx, heightPx);
        var (w, h) = Interface(widthPx, heightPx, scale);
        return L.F(Strings.Bar_ScreenLine, widthPx, heightPx, percent, w, h);
    }
}
