using Hotline.Core.Activation;

namespace Hotline.Core.Settings;

public sealed class ActivationSettings
{
    public KeyAction Tap { get; set; } = KeyAction.TogglePopup;
    public KeyAction Hold { get; set; } = KeyAction.ShowPopup;
    /// <summary>Optional extra hotkey, e.g. "Ctrl+Alt+H". Null or empty = off.</summary>
    public string? FallbackHotkey { get; set; }
}
