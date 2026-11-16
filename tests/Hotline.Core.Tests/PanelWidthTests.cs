using System.Text.Json;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class PanelWidthTests
{
    [Theory]
    [InlineData(320, 1, 320)]
    [InlineData(320, 2, 640)]
    [InlineData(321, 1.5, 482)]
    [InlineData(100, 2, 400)]
    public void Drag_minimum_converts_scaled_pixels_to_real_pixels(int minimum, double scale, int expected) =>
        Assert.Equal(expected, PanelWidth.MinimumPixels(new WindowSettings { MinWidth = minimum }, scale));

    [Theory]
    [InlineData("200", 200, false)]
    [InlineData("640.5", 640.5, false)]
    [InlineData("4000", 4000, false)]
    [InlineData("20%", 20, true)]
    [InlineData("40.5%", 40.5, true)]
    [InlineData("90%", 90, true)]
    public void Pixels_and_percent_round_trip_in_their_json_forms(string text, double number, bool percent)
    {
        Assert.True(PanelWidth.TryParse(text, out var width));
        Assert.Equal((number, percent), (width.Value, width.IsPercent));
        var json = JsonSerializer.Serialize(width);
        Assert.Equal(percent ? JsonValueKind.String : JsonValueKind.Number, JsonDocument.Parse(json).RootElement.ValueKind);
        Assert.Equal(width, JsonSerializer.Deserialize<PanelWidth>(json));
    }

    [Theory]
    [InlineData("199")]
    [InlineData("4001")]
    [InlineData("19%")]
    [InlineData("90.1%")]
    [InlineData("NaN")]
    [InlineData("1e3")]
    [InlineData("40 %")]
    public void Invalid_width_text_is_refused(string text) => Assert.False(PanelWidth.TryParse(text, out _));

    [Theory]
    [InlineData("40%", null, 1920, 1, false, 768)]
    [InlineData("40%", null, 2880, 2, false, 1152)]
    [InlineData("640", null, 2880, 2, false, 1280)]
    [InlineData("90%", null, 2880, 2, false, 1600)]
    [InlineData("20%", null, 1000, 2, false, 640)]
    [InlineData("4000", null, 300, 2, false, 300)]
    [InlineData("40%", "700", 2880, 2, true, 1400)]
    [InlineData("640", "60%", 2880, 2, true, 1600)]
    [InlineData("40%", null, 2880, 2, true, 1152)]
    public void Resolution_uses_scaled_work_area_clamps_and_monitor_scale(string opening, string? chat, int work, double scale, bool chatting, int expected)
    {
        PanelWidth.TryParse(opening, out var width);
        var settings = new WindowSettings { Width = width, ChatWidth = PanelWidth.TryParse(chat, out var chatWidth) ? chatWidth : null };
        Assert.Equal(expected, PanelWidth.ResolvePixels(settings, work, scale, chatting));
    }
}
