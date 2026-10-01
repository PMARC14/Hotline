using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class KeyEventDeduperTests
{
    private readonly ManualTimeProvider _clock = new();
    private KeyEventDeduper New() => new(_clock, TimeSpan.FromMilliseconds(1000));

    [Fact]
    public void First_event_is_handled()
        => Assert.True(New().ShouldHandle(KeyEvent.Tap, KeySource.FastPath));

    [Fact]
    public void Same_press_from_other_channel_within_window_is_dropped()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.Tap, KeySource.FastPath);
        _clock.Advance(TimeSpan.FromMilliseconds(400)); // protocol launch + redirect latency
        Assert.False(d.ShouldHandle(KeyEvent.Tap, KeySource.Protocol));
    }

    [Fact]
    public void Same_press_from_other_channel_after_window_is_handled()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.Tap, KeySource.Protocol);
        _clock.Advance(TimeSpan.FromMilliseconds(1001));
        Assert.True(d.ShouldHandle(KeyEvent.Tap, KeySource.FastPath));
    }

    [Fact]
    public void Rapid_repeats_from_same_channel_are_real_presses()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.Tap, KeySource.FastPath);
        _clock.Advance(TimeSpan.FromMilliseconds(80));
        Assert.True(d.ShouldHandle(KeyEvent.Tap, KeySource.FastPath));
    }

    [Fact]
    public void Different_event_within_window_is_handled()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.HoldStart, KeySource.FastPath);
        _clock.Advance(TimeSpan.FromMilliseconds(10));
        Assert.True(d.ShouldHandle(KeyEvent.HoldStop, KeySource.Protocol));
    }

    [Fact]
    public void Dropped_duplicate_does_not_extend_the_window()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.Tap, KeySource.FastPath);          // t=0 handled
        _clock.Advance(TimeSpan.FromMilliseconds(600));
        Assert.False(d.ShouldHandle(KeyEvent.Tap, KeySource.Protocol)); // t=600 dropped
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(d.ShouldHandle(KeyEvent.Tap, KeySource.Protocol));  // t=1100 > 1000 from the handled one
    }
}
