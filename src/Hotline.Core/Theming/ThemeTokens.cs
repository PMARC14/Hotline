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
    /// <summary>Windows' body text size; the app scales it with the system text-size setting.</summary>
    public const int DefaultFontSize = 14;

    public static ThemeTokens Dark { get; } = new(
        "#FFF3F3F3", "#FFA8A8A8", "#FF8B7CFF", "#0FFFFFFF", "#1AFFFFFF", "#1FFFFFFF", "#388B7CFF", "#59000000", DefaultFont, 14, 8) { SolidBackground = "#FF202020" };

    public static ThemeTokens Light { get; } = new(
        "#FF1A1A1A", "#FF5C5C5C", "#FF5B4BF5", "#0A000000", "#12000000", "#1A000000", "#245B4BF5", "#0D000000", DefaultFont, 14, 8) { SolidBackground = "#FFF3F3F3" };

    /// <summary>Opaque panel background for the Solid backdrop (follows the chosen theme, not the OS theme).</summary>
    public string SolidBackground { get; init; } = "#FF202020";

    /// <summary>True for a Windows contrast theme: every colour comes from the system, nothing is translucent.</summary>
    public bool HighContrast { get; init; }

    /// <summary>
    /// The panel's tokens: the built-in light/dark set (or a contrast theme's system colours) with the user's font and
    /// accent applied (unsafe font names are ignored). <paramref name="windows"/> is what Windows reports; null = unknown.
    /// </summary>
    public static ThemeTokens For(bool dark, WindowSettings window, WindowsColors? windows = null)
    {
        var baseTokens = windows?.Contrast is { } c ? Contrast(c) // Windows' contrast theme always wins
            : window.Theme == ThemeChoice.HighContrast ? Contrast(HighContrastColors)
            : dark ? Dark : Light;
        var family = window.FontFamily?.Trim();
        var font = string.IsNullOrEmpty(family) || !SafeFont().IsMatch(family) ? DefaultFont : $"{family}, {DefaultFont}";
        var tokens = baseTokens with { Font = font, FontSizePx = window.FontSize ?? DefaultFontSize };
        if (tokens.HighContrast) return tokens; // the contrast theme's colours win over the user's accent
        var accent = AccentChoice.Resolve(window.Accent, dark, windows);
        if (accent is not null && !ThemeColor.TryParse(accent, out _)) accent = null; // a colour Windows reported that we can't read
        return accent is null ? tokens : tokens with { Accent = accent, UserBubble = ThemeColor.WithAlpha(accent, dark ? (byte)0x38 : (byte)0x24) };
    }

    /// <summary>A contrast theme: text, links and box outlines in the system colours, at full strength.</summary>
    private static ThemeTokens Contrast(ContrastColors c) => new(
        c.WindowText, c.WindowText, c.Hotlight, c.Window, c.ButtonFace, c.WindowText, c.Window, c.ButtonFace, DefaultFont, DefaultFontSize, 8)
    { SolidBackground = c.Window, HighContrast = true };

    /// <summary>Hotline's own High contrast theme (Theme › High contrast): white on black, cyan links and accent.</summary>
    public static ContrastColors HighContrastColors { get; } = new(
        Window: "#FF000000", WindowText: "#FFFFFFFF", Highlight: "#FF1AEBFF", HighlightText: "#FF000000", Hotlight: "#FF1AEBFF",
        GrayText: "#FFBFBFBF", ButtonFace: "#FF000000", ButtonText: "#FFFFFFFF");

    /// <summary>
    /// The backdrop actually drawn: Solid with a contrast theme (Windows' or Hotline's own) or when Windows'
    /// transparency effects are off (as Windows' own apps do), otherwise the user's choice.
    /// </summary>
    public static BackdropKind EffectiveBackdrop(BackdropKind chosen, WindowsColors? windows, ThemeChoice theme = ThemeChoice.System)
        => windows is { Contrast: not null } or { Transparency: false } || theme == ThemeChoice.HighContrast ? BackdropKind.Solid : chosen;

    [GeneratedRegex(@"^[\w ,.\-]+$")]
    private static partial Regex SafeFont();
}

/// <summary>Windows' colours and switches (Personalization › Colors, Accessibility › Contrast themes), read by the App.</summary>
/// <param name="AccentLight2">The accent shade Windows uses on dark backgrounds.</param>
/// <param name="AccentDark1">The accent shade Windows uses on light backgrounds.</param>
/// <param name="Contrast">The contrast theme's colours; null when none is on.</param>
/// <param name="Transparency">Windows' "Transparency effects" switch.</param>
public sealed record WindowsColors(string AccentLight2, string AccentDark1, ContrastColors? Contrast, bool Transparency);

/// <summary>A contrast theme's system colours (#AARRGGBB).</summary>
public sealed record ContrastColors(string Window, string WindowText, string Highlight, string HighlightText, string Hotlight,
    string GrayText, string ButtonFace, string ButtonText);
