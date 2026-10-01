namespace Hotline.Core.Activation;

/// <summary>
/// One physical key press can arrive via both the fast-path window message and protocol
/// activation. Drops an event identical to the last handled one within <paramref name="window"/>.
/// </summary>
public sealed class KeyEventDeduper(TimeProvider clock, TimeSpan window)
{
    private KeyEvent? _last;
    private DateTimeOffset _lastAt;

    public bool ShouldHandle(KeyEvent e)
    {
        var now = clock.GetUtcNow();
        if (_last == e && now - _lastAt < window)
            return false;
        _last = e;
        _lastAt = now;
        return true;
    }
}
