using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>The panel opens narrow and grows wider when a chat starts (window.chatWidthPercent / chatWidth), like the height.</summary>
public sealed class ChatWidthTests : TempDirTest
{

    private HotlineSettings LoadJson(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), json);
        return new SettingsStore(_dir).Load();
    }

    [Fact]
    public void By_default_the_panel_does_not_grow_wider() => Assert.Null(new HotlineSettings().Window.ChatWidth);

    [Theory]
    [InlineData("50%")]
    [InlineData("640")]
    public void One_chat_width_box_accepts_pixels_or_percent(string text)
    {
        var settings = new HotlineSettings();
        var width = SettingsSchema.Items.OfType<TextItem>().Single(i => i.Header == nameof(Strings.Set_ChatWidth));
        Assert.True(width.Validate!(text));
        width.Set(settings, text);
        Assert.Equal(text, width.Get(settings));
        width.Set(settings, "");
        Assert.Null(settings.Window.ChatWidth);
    }

    // ---- which width ----

    // Real pixels throughout; the opening minimum is 460 and the maximum is 1000.
    [Theory]
    [InlineData(null, 700, 0, 1920, 460, false, 700)]
    [InlineData(null, 700, 0, 1920, 460, true, null)]
    [InlineData(null, null, 50, 1920, 460, true, null)]
    [InlineData(null, null, 0, 1920, 460, false, null)]
    [InlineData(null, 5000, 0, 1920, 460, false, 1000)]
    [InlineData(null, 100, 0, 1920, 460, false, null)]
    [InlineData(null, null, 40, 1920, 460, false, 768)]
    [InlineData(null, null, 80, 1920, 460, false, 1000)]
    [InlineData(null, null, 50, 3840, 460, false, 1000)]
    [InlineData(null, null, 50, 1400, 460, false, 700)]
    [InlineData(null, 700, 80, 1920, 460, false, 700)]
    [InlineData(null, null, 20, 1920, 480, false, null)]
    [InlineData(null, 480, 0, 1920, 480, false, null)]
    [InlineData(null, 300, 0, 1920, 480, false, null)]
    [InlineData(null, 481, 0, 1920, 480, false, 481)]
    [InlineData(600, 900, 60, 1920, 460, false, null)]
    public void Chat_width_respects_manual_sizes_limits_and_the_opening_width(
        int? width, int? chatWidth, double percent, int workPx, int start, bool byHand, int? expected)
        => Assert.Equal(expected, PopupGeometry.ChatWidthPx(width, chatWidth, percent, workPx, 460, 1000, start, byHand));

    // ---- the rectangle ----

    [Theory]
    [InlineData(0, 730, 800, 460, 100, 700, 610, 700, true)]
    [InlineData(0, 0, 0, 460, 100, 700, 0, 700, false)]
    [InlineData(0, 1460, 0, 460, 100, 700, 1220, 700, false)]
    [InlineData(0, 100, 5, 460, 10, 5000, 0, 1920, false)]
    [InlineData(0, 730, 800, 460, 100, 460, 730, 460, true)]
    [InlineData(0, 730, 800, 460, 100, 100, 730, 460, true)]
    [InlineData(-1920, -1900, 0, 460, 100, 700, -1920, 700, false)]
    public void Growing_respects_the_centre_work_area_and_current_width(
        int workX, int barX, int barY, int barW, int barH, int targetPx, int expectedX, int expectedW, bool keepsCentre)
    {
        var bar = new RectI(barX, barY, barW, barH);
        var grown = PopupGeometry.GrowWidth(bar, targetPx, new RectI(workX, 0, 1920, 1080));
        Assert.Equal(new RectI(expectedX, barY, expectedW, barH), grown);
        if (keepsCentre) Assert.Equal(bar.X + bar.Width / 2, grown.X + grown.Width / 2);
    }
}
