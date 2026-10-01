namespace Hotline.Core.Activation;

/// <summary>
/// Clicking the tray icon (or the shell taking focus on a key press) deactivates the popup,
/// which hides it before the toggle request arrives. Treat "hidden within <paramref name="grace"/>"
/// as "was visible" so the toggle closes it instead of re-opening it.
/// </summary>
public sealed class PopupToggleGuard(TimeProvider clock, TimeSpan grace)
{
    private DateTimeOffset _hiddenAt = DateTimeOffset.MinValue;

    public void NoteHidden() => _hiddenAt = clock.GetUtcNow();

    public bool ShouldShowOnToggle(bool isVisible)
        => !isVisible && clock.GetUtcNow() - _hiddenAt > grace;
}
