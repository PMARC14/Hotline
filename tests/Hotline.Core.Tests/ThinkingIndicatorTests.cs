using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class ThinkingIndicatorTests
{
    [Theory]
    [InlineData(true, true, 700, 700, true)]    // no text for the delay: spin
    [InlineData(true, true, 699, 700, false)]   // text just came
    [InlineData(true, false, 5000, 700, false)] // finished, cancelled or failed
    [InlineData(false, true, 5000, 700, false)] // turned off
    [InlineData(true, true, 0, 0, true)]        // delay 0: from the start
    public void Shows_only_while_streaming_without_text_for_the_delay(bool enabled, bool streaming, long since, int delay, bool expected)
        => Assert.Equal(expected, ThinkingIndicator.Show(enabled, streaming, since, delay));

    [Fact]
    public void Defaults_and_clamping()
    {
        var s = new HotlineSettings();
        Assert.True(s.Chat.ThinkingIndicator);
        Assert.Equal(700, s.Chat.ThinkingDelayMs);
        s.Chat.ThinkingDelayMs = 99_999;
        Assert.Equal(10_000, SettingsStore.Normalize(s).Chat.ThinkingDelayMs);
    }
}
