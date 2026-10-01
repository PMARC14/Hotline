using System.Globalization;
using System.Text.RegularExpressions;
using Hotline.Core.Settings;

namespace Hotline.Core.Theming;

/// <summary>
/// Design tokens for the native chat UI (spec: Theming). Colors are "#AARRGGBB" / "#RRGGBB"; the App turns them
/// into brushes. Font is a XAML family list. User theme files (later) supply the same shape.
/// </summary>
public sealed partial record ThemeTokens(
    string Text, string Muted, string Accent, string Surface, string SurfaceStrong, string Border,
    string UserBubble, string CodeBackground, string Font, int FontSizePx, int RadiusPx)
{
    private const string DefaultFont = "Segoe UI Variable Text, Segoe UI";

    public static ThemeTokens Dark { get; } = new(
        "#FFF3F3F3", "#FFA8A8A8", "#FF8B7CFF", "#0FFFFFFF", "#1AFFFFFF", "#1FFFFFFF", "#388B7CFF", "#59000000", DefaultFont, 14, 8);

    public static ThemeTokens Light { get; } = new(
        "#FF1A1A1A", "#FF5C5C5C", "#FF5B4BF5", "#0A000000", "#12000000", "#1A000000", "#245B4BF5", "#0D000000", DefaultFont, 14, 8);

    /// <summary>Built-in tokens with the user's font settings applied (unsafe font names are ignored).</summary>
    public static ThemeTokens For(bool dark, WindowSettings window)
    {
        var baseTokens = dark ? Dark : Light;
        var family = window.FontFamily?.Trim();
        var font = string.IsNullOrEmpty(family) || !SafeFont().IsMatch(family) ? baseTokens.Font : $"{family}, {DefaultFont}";
        return baseTokens with { Font = font, FontSizePx = window.FontSize };
    }

    [GeneratedRegex(@"^[\w ,.\-]+$")]
    private static partial Regex SafeFont();
}

public static class ThemeColor
{
    public static (byte A, byte R, byte G, byte B) Parse(string hex)
    {
        if (hex.Length is not (7 or 9) || hex[0] != '#' || !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"Not a #RRGGBB/#AARRGGBB color: {hex}");
        if (hex.Length == 7) v |= 0xFF000000;
        return ((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}
