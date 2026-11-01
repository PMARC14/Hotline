using Hotline.Core.Settings;
using Hotline.Core.Theming;

namespace Hotline.Core.Tests;

public class ThemeColorsTests
{
    private static readonly ContrastColors Aquatic = new(
        Window: "#FF202020", WindowText: "#FFFFFFFF", Highlight: "#FF8EE3F0", HighlightText: "#FF263B50",
        Hotlight: "#FF75E9FC", GrayText: "#FFA6A6A6", ButtonFace: "#FF202020", ButtonText: "#FFFFFFFF");

    private static WindowsColors Windows(ContrastColors? contrast = null, bool transparency = true) =>
        new(AccentLight2: "#FF99EBFF", AccentDark1: "#FF005A9E", Contrast: contrast, Transparency: transparency);

    private static WindowSettings Window(string? accent = null) => new() { Accent = accent ?? AccentChoice.Hotline };

    [Fact]
    public void Default_accent_is_hotline_purple_in_both_themes()
    {
        Assert.Equal(AccentChoice.Hotline, new WindowSettings().Accent);
        Assert.Equal(ThemeTokens.Dark.Accent, ThemeTokens.For(true, Window(), Windows()).Accent);
        Assert.Equal(ThemeTokens.Light.Accent, ThemeTokens.For(false, Window(), Windows()).Accent);
        Assert.False(ThemeTokens.For(true, Window(), Windows()).HighContrast);
    }

    [Theory]
    [InlineData(true, true, "#FF99EBFF")]   // lighter shade on dark
    [InlineData(false, true, "#FF005A9E")]  // darker shade on light
    [InlineData(true, false, null)]         // no system colours: falls back to Hotline's
    public void Windows_accent_follows_the_system_shade_or_falls_back_to_hotline(bool dark, bool haveSystemColours, string? expected)
        => Assert.Equal(expected ?? ThemeTokens.Dark.Accent,
            ThemeTokens.For(dark, Window(AccentChoice.Windows), haveSystemColours ? Windows() : null).Accent);

    [Theory]
    [InlineData("#E81123", "#FFE81123")]
    [InlineData("#e81123", "#FFE81123")]
    [InlineData(" #00B294 ", "#FF00B294")]
    public void Custom_accent_is_used_as_is(string setting, string expected)
        => Assert.Equal(expected, ThemeTokens.For(true, Window(setting), Windows()).Accent);

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#80E81123")] // no transparency: the accent is drawn on buttons
    [InlineData("")]
    public void Invalid_accent_falls_back_to_hotline(string setting)
    {
        Assert.False(AccentChoice.IsCustomColor(setting));
        Assert.Equal(ThemeTokens.Dark.Accent, ThemeTokens.For(true, Window(setting), Windows()).Accent);
    }

    [Fact]
    public void User_bubble_is_a_tint_of_the_accent()
    {
        Assert.Equal("#38E81123", ThemeTokens.For(true, Window("#E81123"), Windows()).UserBubble);
        Assert.Equal("#24E81123", ThemeTokens.For(false, Window("#E81123"), Windows()).UserBubble);
    }

    [Theory]
    [InlineData("#FFFFD700", "#FF000000")] // gold: dark text
    [InlineData("#FF99EBFF", "#FF000000")]
    [InlineData("#FF005A9E", "#FFFFFFFF")] // dark blue: white text
    [InlineData("#FF5B4BF5", "#FFFFFFFF")]
    public void Text_on_the_accent_is_whichever_of_black_or_white_reads_better(string accent, string expected)
        => Assert.Equal(expected, ThemeColor.OnColor(accent));

    [Fact]
    public void Contrast_theme_uses_the_system_colours_and_keeps_the_font()
    {
        var t = ThemeTokens.For(true, new WindowSettings { FontFamily = "Cascadia Code", FontSize = 16, Accent = "#E81123" }, Windows(Aquatic));
        Assert.True(t.HighContrast);
        Assert.Equal("#FFFFFFFF", t.Text);
        Assert.Equal("#FFFFFFFF", t.Muted); // no dimmed text in a contrast theme
        Assert.Equal("#FF75E9FC", t.Accent); // the theme's link colour, not the user's accent
        Assert.Equal("#FF202020", t.SolidBackground);
        Assert.Equal("#FFFFFFFF", t.Border);
        Assert.StartsWith("Cascadia Code", t.Font);
        Assert.Equal(16, t.FontSizePx);
    }

    [Fact]
    public void Hotlines_own_high_contrast_theme_is_white_on_black_with_a_cyan_accent()
    {
        var t = ThemeTokens.For(true, new WindowSettings { Theme = ThemeChoice.HighContrast, Accent = "#E81123" }, Windows());
        Assert.True(t.HighContrast);
        Assert.Equal("#FF000000", t.SolidBackground);
        Assert.Equal("#FFFFFFFF", t.Text);
        Assert.Equal("#FFFFFFFF", t.Muted);
        Assert.Equal(ThemeTokens.HighContrastColors.Hotlight, t.Accent); // not the user's accent
    }

    [Fact]
    public void A_windows_contrast_theme_wins_over_hotlines_own()
        => Assert.Equal("#FF75E9FC", ThemeTokens.For(true, new WindowSettings { Theme = ThemeChoice.HighContrast }, Windows(Aquatic)).Accent);

    [Theory]
    [InlineData(BackdropKind.Acrylic, true, false, ThemeChoice.System, BackdropKind.Acrylic)]
    [InlineData(BackdropKind.Mica, true, false, ThemeChoice.System, BackdropKind.Mica)]
    [InlineData(BackdropKind.Acrylic, false, false, ThemeChoice.System, BackdropKind.Solid)] // Windows' transparency effects off
    [InlineData(BackdropKind.AcrylicThin, true, true, ThemeChoice.System, BackdropKind.Solid)] // contrast theme
    [InlineData(BackdropKind.Solid, true, false, ThemeChoice.System, BackdropKind.Solid)]
    [InlineData(BackdropKind.Acrylic, true, false, ThemeChoice.HighContrast, BackdropKind.Solid)] // Hotline's own high contrast
    public void Windows_settings_can_force_a_solid_backdrop(BackdropKind chosen, bool transparency, bool contrast, ThemeChoice theme, BackdropKind expected)
        => Assert.Equal(expected, ThemeTokens.EffectiveBackdrop(chosen, Windows(contrast ? Aquatic : null, transparency), theme));

    [Fact]
    public void Without_system_colours_the_chosen_backdrop_stays()
        => Assert.Equal(BackdropKind.Mica, ThemeTokens.EffectiveBackdrop(BackdropKind.Mica, null));
}
