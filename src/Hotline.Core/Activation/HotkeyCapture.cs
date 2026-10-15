namespace Hotline.Core.Activation;

/// <summary>
/// Setting a global hotkey by pressing it, like a game's key binder: what one key press (with the modifiers held)
/// means. Esc cancels and Backspace/Delete clear (both without modifiers); a modifier on its own keeps waiting for the
/// key. The result is the same text settings.json holds, so only keys that text can name are accepted.
/// </summary>
public static class HotkeyCapture
{
    public enum Outcome { Bound, Waiting, Cancel, Clear, NeedsModifier, Unsupported }

    /// <param name="Text">The shortcut as settings.json writes it (e.g. "Ctrl+Alt+H"); only for <see cref="Outcome.Bound"/>.</param>
    public sealed record Result(Outcome Outcome, string? Text = null);

    private const uint VkBackspace = 0x08, VkEsc = 0x1B, VkDelete = 0x2E;

    // Shift, Ctrl, Alt (generic and left/right), Win, and the IME / Caps Lock keys that never make a shortcut.
    private static readonly HashSet<uint> ModifierKeys = [0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C, 0x14];

    public static Result Press(HotkeyModifiers held, uint virtualKey)
    {
        if (ModifierKeys.Contains(virtualKey)) return new(Outcome.Waiting);
        if (held == HotkeyModifiers.None && virtualKey == VkEsc) return new(Outcome.Cancel);
        if (held == HotkeyModifiers.None && virtualKey is VkBackspace or VkDelete) return new(Outcome.Clear);
        var text = new Hotkey(held, virtualKey).ToString();
        if (Hotkey.TryParse(text, out var parsed) && parsed == new Hotkey(held, virtualKey)) return new(Outcome.Bound, text);
        // Text the setting can't hold back (an unnamed key) is refused; a nameable key alone needs a modifier.
        return held == HotkeyModifiers.None && Hotkey.TryParseShortcut(text, out _) ? new(Outcome.NeedsModifier) : new(Outcome.Unsupported);
    }
}
