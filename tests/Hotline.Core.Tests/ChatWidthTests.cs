using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>The panel opens narrow and grows wider when a chat starts (window.chatWidthPercent / chatWidth), like the height.</summary>
public sealed class ChatWidthTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private HotlineSettings LoadJson(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), json);
        return new SettingsStore(_dir).Load();
    }

    // ---- settings ----

    [Fact]
    public void By_default_the_panel_does_not_grow_wider()
    {
        var w = new HotlineSettings().Window;
        Assert.Equal(0, w.ChatWidthPercent);
        Assert.Null(w.ChatWidth);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(5, 20)]
    [InlineData(45, 45)]
    [InlineData(500, 90)]
    public void The_chat_width_percent_is_zero_or_twenty_to_ninety(double given, double kept)
        => Assert.Equal(kept, LoadJson($$"""{ "window": { "chatWidthPercent": {{given}} } }""").Window.ChatWidthPercent);

    [Fact]
    public void An_explicit_chat_width_is_clamped_and_null_stays_null()
    {
        Assert.Equal(320, LoadJson("""{ "window": { "chatWidth": 10 } }""").Window.ChatWidth);
        Assert.Equal(8000, LoadJson("""{ "window": { "chatWidth": 99999 } }""").Window.ChatWidth);
        Assert.Equal(700, LoadJson("""{ "window": { "chatWidth": 700 } }""").Window.ChatWidth);
        Assert.Null(LoadJson("""{ "window": { "chatWidth": null } }""").Window.ChatWidth);
    }

    [Fact]
    public void The_settings_page_has_both_items_and_writes_them()
    {
        var s = new HotlineSettings();
        var percent = SettingsSchema.Items.OfType<NumberItem>().Single(i => i.Header == nameof(Strings.Set_ChatWidthPercent));
        var width = SettingsSchema.Items.OfType<NumberItem>().Single(i => i.Header == nameof(Strings.Set_ChatWidth));
        percent.Set(s, 50);
        width.Set(s, 640);
        Assert.Equal((50.0, 640), (s.Window.ChatWidthPercent, s.Window.ChatWidth));
        Assert.Equal(640, width.Get(s));
        width.Set(s, 0);
        Assert.Null(s.Window.ChatWidth);
    }

    // ---- which width ----

    // A 1920 px wide work area at 100%: the 25% start width is 480 DIP, clamped to the 460 minimum -> 480.
    private static int? Chat(int? width = null, int? chatWidth = null, double percent = 0, int workPx = 1920, double scale = 1, int start = 460, int min = 460, int max = 1000)
        => PopupGeometry.ChatWidthDip(width, chatWidth, percent, workPx, scale, min, max, start);

    [Fact]
    public void No_chat_width_set_means_no_growth() => Assert.Null(Chat());

    [Fact]
    public void A_percentage_is_taken_of_the_work_area_and_clamped_to_min_and_max()
    {
        Assert.Equal(768, Chat(percent: 40));
        Assert.Equal(1000, Chat(percent: 80)); // 1536 -> max
        Assert.Equal(960, Chat(percent: 50, workPx: 3840, scale: 2)); // DIP, not pixels
    }

    [Fact]
    public void An_explicit_chat_width_beats_the_percentage()
        => Assert.Equal(700, Chat(chatWidth: 700, percent: 80));

    [Fact]
    public void A_target_not_wider_than_the_start_width_means_no_growth()
    {
        Assert.Null(Chat(percent: 20, start: 480)); // 384 -> clamped to 460, under the start
        Assert.Null(Chat(chatWidth: 480, start: 480));
        Assert.Null(Chat(chatWidth: 300, start: 480));
        Assert.Equal(481, Chat(chatWidth: 481, start: 480));
    }

    [Fact]
    public void A_width_set_by_hand_or_in_the_file_means_no_growth()
        => Assert.Null(Chat(width: 600, chatWidth: 900, percent: 60));

    // ---- the rectangle ----

    private static readonly RectI Work = new(0, 0, 1920, 1080);

    [Fact]
    public void Growing_keeps_the_horizontal_centre()
    {
        var bar = new RectI(730, 800, 460, 100);
        var grown = PopupGeometry.GrowWidth(bar, 700, Work);
        Assert.Equal(new RectI(610, 800, 700, 100), grown);
        Assert.Equal(bar.X + bar.Width / 2, grown.X + grown.Width / 2);
    }

    [Fact]
    public void Growing_at_the_work_area_edge_slides_the_panel_back_inside()
    {
        Assert.Equal(new RectI(0, 0, 700, 100), PopupGeometry.GrowWidth(new RectI(0, 0, 460, 100), 700, Work));
        Assert.Equal(new RectI(1220, 0, 700, 100), PopupGeometry.GrowWidth(new RectI(1460, 0, 460, 100), 700, Work));
    }

    [Fact]
    public void Growing_never_exceeds_the_work_area()
        => Assert.Equal(new RectI(0, 5, 1920, 10), PopupGeometry.GrowWidth(new RectI(100, 5, 460, 10), 5000, Work));

    [Fact]
    public void A_target_at_or_under_the_current_width_changes_nothing()
    {
        var bar = new RectI(730, 800, 460, 100);
        Assert.Equal(bar, PopupGeometry.GrowWidth(bar, 460, Work));
        Assert.Equal(bar, PopupGeometry.GrowWidth(bar, 100, Work));
    }

    [Fact]
    public void A_work_area_on_a_second_monitor_with_a_negative_origin_is_respected()
    {
        var work = new RectI(-1920, 0, 1920, 1080);
        Assert.Equal(new RectI(-1920, 0, 700, 100), PopupGeometry.GrowWidth(new RectI(-1900, 0, 460, 100), 700, work));
    }
}
