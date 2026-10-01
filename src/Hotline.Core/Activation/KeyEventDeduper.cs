namespace Hotline.Core.Activation;

/// <summary>
/// One physical key press could arrive via both the fast-path window message and protocol
/// activation. Drops an event identical to the last handled one when it comes from a
/// <em>different</em> channel within <paramref name="window"/>. Repeats on the same channel are
/// genuine presses and always pass.
/// </summary>
public sealed class KeyEventDeduper(TimeProvider clock, TimeSpan window)
{
    private KeyEvent? _last;
    private KeySource _lastSource;
    private DateTimeOffset _lastAt;

    public bool ShouldHandle(KeyEvent e, KeySource source)
    {
        var now = clock.GetUtcNow();
        if (_last == e && _lastSource != source && now - _lastAt < window)
            return false;
        _last = e;
        _lastSource = source;
        _lastAt = now;
        return true;
    }
}
