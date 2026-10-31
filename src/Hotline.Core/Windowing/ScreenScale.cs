using Hotline.Core.Text.Localization;

namespace Hotline.Core.Windowing;

/// <summary>
/// The one sentence Settings shows above the button rows: what Windows' display scaling does to this screen, so a width in
/// percent (of the real pixels) and sizes in cells (scaled units) are easy to relate.
/// </summary>
public static class ScreenScale
{
    /// <summary>Windows' interface size (the scaled units) of a screen of <paramref name="widthPx"/> × <paramref name="heightPx"/> real pixels at <paramref name="scale"/> (1 = 100%).</summary>
    public static (int Width, int Height) Interface(int widthPx, int heightPx, double scale)
    {
        scale = scale is > 0.5 and < 8 ? scale : 1;
        return ((int)Math.Round(widthPx / scale), (int)Math.Round(heightPx / scale));
    }

    /// <summary>"Windows display scaling is 200%: this screen is 2880 × 1800 pixels, Windows draws the interface at 1440 × 900 ...".</summary>
    public static string Note(int widthPx, int heightPx, double scale)
    {
        scale = scale is > 0.5 and < 8 ? scale : 1;
        var percent = (int)Math.Round(scale * 100);
        if (percent == 100) return L.F(Strings.Bar_ScaleNote100, widthPx, heightPx);
        var (w, h) = Interface(widthPx, heightPx, scale);
        var sharper = Math.Round(scale, 2).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        return L.F(Strings.Bar_ScaleNote, percent, widthPx, heightPx, w, h, sharper);
    }
}
