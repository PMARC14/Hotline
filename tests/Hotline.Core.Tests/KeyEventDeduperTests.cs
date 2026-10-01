using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class KeyEventDeduperTests
{
    private readonly ManualTimeProvider _clock = new();
    private KeyEventDeduper New() => new(_clock, TimeSpan.FromMilliseconds(150));

    [Fact]
    public void First_event_is_handled()
        => Assert.True(New().ShouldHandle(KeyEvent.Tap));

    [Fact]
    public void Same_event_within_window_is_dropped()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.Tap);
        _clock.Advance(TimeSpan.FromMilliseconds(40));
        Assert.False(d.ShouldHandle(KeyEvent.Tap));
    }

    [Fact]
    public void Same_event_after_window_is_handled()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.Tap);
        _clock.Advance(TimeSpan.FromMilliseconds(151));
        Assert.True(d.ShouldHandle(KeyEvent.Tap));
    }

    [Fact]
    public void Different_event_within_window_is_handled()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.HoldStart);
        _clock.Advance(TimeSpan.FromMilliseconds(10));
        Assert.True(d.ShouldHandle(KeyEvent.HoldStop));
    }

    [Fact]
    public void Dropped_duplicate_does_not_extend_the_window()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.Tap);                       // t=0
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.False(d.ShouldHandle(KeyEvent.Tap));         // t=100 dropped
        _clock.Advance(TimeSpan.FromMilliseconds(60));
        Assert.True(d.ShouldHandle(KeyEvent.Tap));          // t=160 > 150 from the handled one
    }
}
