using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public sealed class VoiceStopPolicyTests
{
    [Fact]
    public void Tap_silence_timeout_is_thirty_seconds()
    {
        Assert.Equal(30.0, VoiceStopPolicy.TapSilenceTimeoutSeconds);
        Assert.Equal(TimeSpan.FromSeconds(30), VoiceStopPolicy.TapSilenceTimeout);
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.0, false)]
    [InlineData(29.9, true)]
    [InlineData(30.0, true)]
    [InlineData(60.0, true)]
    [InlineData(60.0, false)]
    public void Hold_mode_never_stops_on_silence_or_deactivation(double seconds, bool panelActive)
    {
        Assert.False(VoiceStopPolicy.ShouldStop(VoiceMode.Hold, seconds, panelActive));
        Assert.Equal(VoiceStopReason.None, VoiceStopPolicy.Decide(VoiceMode.Hold, seconds, panelActive));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(5.0)]
    [InlineData(29.9)]
    [InlineData(30.0)]
    [InlineData(45.0)]
    public void Tap_mode_stops_immediately_when_panel_loses_activation(double seconds)
    {
        Assert.True(VoiceStopPolicy.ShouldStop(VoiceMode.Tap, seconds, false));
        Assert.True(VoiceStopPolicy.ShouldStop(VoiceMode.Tap, false));
        Assert.Equal(VoiceStopReason.Deactivated, VoiceStopPolicy.Decide(VoiceMode.Tap, seconds, false));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    [InlineData(15.0)]
    [InlineData(29.9)]
    public void Tap_mode_continues_while_panel_active_and_silence_below_timeout(double seconds)
    {
        Assert.False(VoiceStopPolicy.ShouldStop(VoiceMode.Tap, seconds, true));
        Assert.False(VoiceStopPolicy.ShouldStop(VoiceMode.Tap, true));
        Assert.Equal(VoiceStopReason.None, VoiceStopPolicy.Decide(VoiceMode.Tap, seconds, true));
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(30.1)]
    [InlineData(60.0)]
    public void Tap_mode_stops_when_silence_reaches_or_exceeds_timeout(double seconds)
    {
        Assert.True(VoiceStopPolicy.ShouldStop(VoiceMode.Tap, seconds, true));
        Assert.Equal(VoiceStopReason.SilenceTimeout, VoiceStopPolicy.Decide(VoiceMode.Tap, seconds, true));
    }

    [Fact]
    public void TimeSpan_overloads_behave_identically_to_seconds()
    {
        Assert.False(VoiceStopPolicy.ShouldStop(VoiceMode.Tap, TimeSpan.FromSeconds(20), true));
        Assert.Equal(VoiceStopReason.None, VoiceStopPolicy.Decide(VoiceMode.Tap, TimeSpan.FromSeconds(20), true));

        Assert.True(VoiceStopPolicy.ShouldStop(VoiceMode.Tap, TimeSpan.FromSeconds(30), true));
        Assert.Equal(VoiceStopReason.SilenceTimeout, VoiceStopPolicy.Decide(VoiceMode.Tap, TimeSpan.FromSeconds(30), true));

        Assert.True(VoiceStopPolicy.ShouldStop(VoiceMode.Tap, TimeSpan.FromSeconds(10), false));
        Assert.Equal(VoiceStopReason.Deactivated, VoiceStopPolicy.Decide(VoiceMode.Tap, TimeSpan.FromSeconds(10), false));

        Assert.False(VoiceStopPolicy.ShouldStop(VoiceMode.Hold, TimeSpan.FromSeconds(50), false));
        Assert.Equal(VoiceStopReason.None, VoiceStopPolicy.Decide(VoiceMode.Hold, TimeSpan.FromSeconds(50), false));
    }
}
