namespace Hotline.Core.Activation;

/// <summary>
/// One physical key press could arrive via both the fast-path window message and protocol
/// activation. Drops an event when the last handled event of the same kind came from a
/// <em>different</em> channel within <paramref name="window"/> (kept per kind, so a quick press and
/// release delivered one way are not replayed by the other channel). Repeats on the same channel are
/// genuine presses and always pass. Called from the UI thread and from the window-message path, hence the lock.
/// </summary>
public sealed class KeyEventDeduper(TimeProvider clock, TimeSpan window)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<KeyEvent, (KeySource Source, DateTimeOffset At)> _last = [];

    public bool ShouldHandle(KeyEvent e, KeySource source)
    {
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            if (_last.TryGetValue(e, out var last) && last.Source != source && now - last.At < window)
                return false;
            _last[e] = (source, now);
            return true;
        }
    }
}
