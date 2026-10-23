using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class KeyEventDeduperTests
{
    private readonly ManualTimeProvider _clock = new();
    private KeyEventDeduper New() => new(_clock, TimeSpan.FromMilliseconds(1000));

    [Theory]
    [InlineData(KeyEvent.Tap, KeySource.FastPath, 400, KeyEvent.Tap, KeySource.Protocol, false)]      // protocol launch + redirect latency
    [InlineData(KeyEvent.Tap, KeySource.Protocol, 1001, KeyEvent.Tap, KeySource.FastPath, true)]      // after the window
    [InlineData(KeyEvent.Tap, KeySource.FastPath, 80, KeyEvent.Tap, KeySource.FastPath, true)]        // same channel: a real repeat
    [InlineData(KeyEvent.HoldStart, KeySource.FastPath, 10, KeyEvent.HoldStop, KeySource.Protocol, true)] // different event
    public void Second_event_is_dropped_only_as_a_duplicate_from_the_other_channel(
        KeyEvent first, KeySource firstSource, int delayMs, KeyEvent second, KeySource secondSource, bool handled)
    {
        var d = New();
        Assert.True(d.ShouldHandle(first, firstSource)); // the first event is always handled
        _clock.Advance(TimeSpan.FromMilliseconds(delayMs));
        Assert.Equal(handled, d.ShouldHandle(second, secondSource));
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
