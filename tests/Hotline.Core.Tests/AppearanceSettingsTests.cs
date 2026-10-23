using Hotline.Core.Activation;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;
using Hotline.Core.Theming;

namespace Hotline.Core.Tests;

public sealed class AppearanceSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private HotlineSettings LoadJson(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), json);
        return new SettingsStore(_dir).Load();
    }

    [Fact]
    public void Defaults()
    {
        var s = new SettingsStore(_dir).Load();
        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
        Assert.Equal(KeyAction.NewChat, s.Activation.Hold);
        Assert.Null(s.Window.FontSize); // Windows default
        Assert.Null(s.Window.FontFamily);
        Assert.Equal(ScrollbarStyle.Auto, s.Window.Scrollbar);
        Assert.Equal(GrowMode.Grow, s.Chat.GrowMode);
    }

    [Fact]
    public void Schema2_customized_hold_is_kept()
        => Assert.Equal(KeyAction.CaptureWindow, LoadJson("""{ "schemaVersion": 2, "activation": { "hold": "captureWindow" } }""").Activation.Hold);

    [Theory]
    [InlineData(4, 10)]
    [InlineData(99, 32)]
    public void Font_size_is_clamped(int value, int expected)
        => Assert.Equal(expected, LoadJson($$"""{ "window": { "fontSize": {{value}} } }""").Window.FontSize);

    [Fact]
    public void Theme_tokens_follow_font_settings()
    {
        var t = ThemeTokens.For(dark: true, new WindowSettings { FontSize = 17, FontFamily = "Cascadia Code" });
        Assert.Equal(17, t.FontSizePx);
        Assert.Equal("Cascadia Code, Segoe UI Variable Text, Segoe UI", t.Font);
        Assert.Equal(ThemeTokens.DefaultFontSize, ThemeTokens.For(dark: false, new WindowSettings()).FontSizePx); // no size set: Windows' size
    }

    [Theory]
    [InlineData("x;}body{evil")]
    [InlineData("<script>")]
    [InlineData("Font\"Name")]
    public void Unsafe_font_family_falls_back_to_default(string family)
        => Assert.Equal(ThemeTokens.Dark.Font, ThemeTokens.For(dark: true, new WindowSettings { FontFamily = family }).Font);

    [Fact]
    public void Number_boxes_step_up_from_automatic_and_clamp_instead_of_switching_off()
    {
        var width = SettingsSchema.Items.OfType<NumberItem>().Single(i => i.Header == nameof(Strings.Set_FixedWidth));
        var chat = SettingsSchema.Items.OfType<NumberItem>().Single(i => i.Header == nameof(Strings.Set_ChatHeight));
        var s = new HotlineSettings();
        width.Set(s, 10);
        chat.Set(s, 10);
        Assert.Equal((320, 60), (s.Window.Width, s.Window.ChatHeight));
        width.Set(s, 0);
        Assert.Null(s.Window.Width);
    }
}
