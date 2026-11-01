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

    /// <summary>
    /// "On this screen (2880 px): 40% = 1152 px (576 scaled units); minimum 384 px, maximum 2304 px": what the width settings come to
    /// on the work area of <paramref name="workWidthPx"/> real pixels (the width is in real pixels; the scaled units are what Windows' scaling makes of them).
    /// </summary>
    public static string WidthLine(int workWidthPx, double scale, double percent, int minPx, int maxPx)
    {
        scale = scale is > 0.5 and < 8 ? scale : 1;
        var px = PopupGeometry.RelativeWidthPx(workWidthPx, percent, minPx, maxPx);
        var scaled = (int)Math.Round(px / scale);
        var share = Math.Round(percent, 2).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        return L.F(Strings.Bar_WidthLine, workWidthPx, share, px, scaled, minPx, maxPx);
    }
}
