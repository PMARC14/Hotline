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
    }

    [Fact]
    public void Windows_accent_uses_the_lighter_shade_on_dark_and_the_darker_on_light()
    {
        Assert.Equal("#FF99EBFF", ThemeTokens.For(true, Window(AccentChoice.Windows), Windows()).Accent);
        Assert.Equal("#FF005A9E", ThemeTokens.For(false, Window(AccentChoice.Windows), Windows()).Accent);
    }

    [Fact]
    public void Windows_accent_without_system_colours_falls_back_to_hotline()
        => Assert.Equal(ThemeTokens.Dark.Accent, ThemeTokens.For(true, Window(AccentChoice.Windows), null).Accent);

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
    public void Normal_themes_are_not_high_contrast()
        => Assert.False(ThemeTokens.For(true, Window(), Windows()).HighContrast);

    [Theory]
    [InlineData(BackdropKind.Acrylic, true, false, BackdropKind.Acrylic)]
    [InlineData(BackdropKind.Mica, true, false, BackdropKind.Mica)]
    [InlineData(BackdropKind.Acrylic, false, false, BackdropKind.Solid)] // Windows' transparency effects off
    [InlineData(BackdropKind.AcrylicThin, true, true, BackdropKind.Solid)] // contrast theme
    [InlineData(BackdropKind.Solid, true, false, BackdropKind.Solid)]
    public void Windows_settings_can_force_a_solid_backdrop(BackdropKind chosen, bool transparency, bool contrast, BackdropKind expected)
        => Assert.Equal(expected, ThemeTokens.EffectiveBackdrop(chosen, Windows(contrast ? Aquatic : null, transparency)));

    [Fact]
    public void Without_system_colours_the_chosen_backdrop_stays()
        => Assert.Equal(BackdropKind.Mica, ThemeTokens.EffectiveBackdrop(BackdropKind.Mica, null));

    [Fact]
    public void Accent_round_trips_through_settings_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(dir);
            var s = store.Load();
            s.Window.Accent = AccentChoice.Windows;
            store.Save(s);
            Assert.Equal(AccentChoice.Windows, new SettingsStore(dir).Load().Window.Accent);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
