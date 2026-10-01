using Hotline.Core.Activation;
using Hotline.Core.Settings;
using Hotline.Core.Theming;
using Hotline.Core.Windowing;

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
        Assert.Equal(14, s.Window.FontSize);
        Assert.Null(s.Window.FontFamily);
        Assert.Equal(ScrollbarStyle.Auto, s.Window.Scrollbar);
        Assert.Equal(GrowMode.Grow, s.Chat.GrowMode);
    }

    [Fact]
    public void Schema2_old_default_hold_migrates_to_new_chat()
        => Assert.Equal(KeyAction.NewChat, LoadJson("""{ "schemaVersion": 2, "activation": { "hold": "showPopup" } }""").Activation.Hold);

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
        var css = ThemeTokens.For(dark: true, new WindowSettings { FontSize = 17, FontFamily = "Cascadia Code" }).ToCss();
        Assert.Contains("--hl-font-size:17px", css);
        Assert.Contains("--hl-font:'Cascadia Code'", css);
    }

    [Fact]
    public void Unsafe_font_family_falls_back_to_default()
        => Assert.DoesNotContain("evil", ThemeTokens.For(dark: true, new WindowSettings { FontFamily = "x;}body{evil" }).ToCss());

    [Fact]
    public void Theme_tokens_include_scrollbar_colors()
    {
        var css = ThemeTokens.Dark.ToCss();
        Assert.Contains("--hl-scrollbar:", css);
        Assert.Contains("--hl-scrollbar-hover:", css);
    }

    [Fact]
    public void Full_grow_mode_jumps_to_max_once_there_is_content()
    {
        var work = new RectI(0, 0, 1920, 1040);
        var bar = new RectI(680, 736, 560, 120);
        Assert.Equal(560, PopupGeometry.GrowUp(bar, 200, 560, work, GrowMode.Full).Height);
        Assert.Equal(120, PopupGeometry.GrowUp(bar, 120, 560, work, GrowMode.Full).Height); // bar only: stays a bar
        Assert.Equal(200, PopupGeometry.GrowUp(bar, 200, 560, work, GrowMode.Grow).Height);
    }
}
