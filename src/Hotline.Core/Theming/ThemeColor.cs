using System.Globalization;

namespace Hotline.Core.Theming;

public static class ThemeColor
{
    /// <summary>The same colour with another alpha.</summary>
    public static string WithAlpha(string hex, byte alpha)
    {
        var (_, r, g, b) = Parse(hex);
        return $"#{alpha:X2}{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>Black or white, whichever has more contrast on <paramref name="hex"/> (WCAG relative luminance).</summary>
    public static string OnColor(string hex)
    {
        var (_, r, g, b) = Parse(hex);
        static double Lin(byte v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        var l = 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
        return (l + 0.05) / 0.05 >= 1.05 / (l + 0.05) ? "#FF000000" : "#FFFFFFFF";
    }

    public static bool TryParse(string? hex, out (byte A, byte R, byte G, byte B) color)
    {
        color = default;
        if (hex is null) return false;
        try { color = Parse(hex); return true; }
        catch (FormatException) { color = default; return false; }
    }

    public static (byte A, byte R, byte G, byte B) Parse(string hex)
    {
        if (hex.Length is not (7 or 9) || hex[0] != '#' || !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"Not a #RRGGBB/#AARRGGBB color: {hex}");
        if (hex.Length == 7) v |= 0xFF000000;
        return ((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}
