using Hotline.Core.Theming;

namespace Hotline.App;

/// <summary>
/// The colour the bar's outlines use when a button names none, resolved in one place for the bar (<see cref="ToolbarRenderer"/>) and
/// Settings' swatches ("Accent", "Like the bar"), so the swatch is the colour that is drawn: a contrast theme's highlight colour, else
/// the panel theme's accent (<see cref="Theme"/>: Hotline's purple, the Windows accent when matching is on, or the user's own), else
/// Windows' accent if the theme gave none.
/// </summary>
internal static class ToolbarAccent
{
    /// <summary>The panel theme's accent (#RRGGBB), set by <see cref="ToolbarRenderer.SetAccent"/> whenever the theme changes; null before a theme set one.</summary>
    public static string? Theme { get; set; }

    private static readonly Windows.UI.ViewManagement.UISettings Ui = new();

    /// <summary>The outline colour for <paramref name="themeAccent"/> (the theme's accent hex).</summary>
    public static Windows.UI.Color Resolve(string? themeAccent)
    {
        try
        {
            if (SystemTheme.Colors.Contrast is { } contrast && ThemeColor.TryParse(contrast.Highlight, out var h))
                return Windows.UI.Color.FromArgb(0xFF, h.R, h.G, h.B);
            if (themeAccent is not null && ThemeColor.TryParse(themeAccent, out var t))
                return Windows.UI.Color.FromArgb(0xFF, t.R, t.G, t.B);
            var accent = Ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent); // SystemAccentColor's source
            return Windows.UI.Color.FromArgb(0xFF, accent.R, accent.G, accent.B);
        }
        catch (Exception) { return Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4); } // WinRT can fail in any form: Windows' default blue
    }

    /// <summary>The colour the bar draws now (<see cref="Resolve"/> of <see cref="Theme"/>).</summary>
    public static Windows.UI.Color Current => Resolve(Theme);
}
