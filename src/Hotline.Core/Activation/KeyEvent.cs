namespace Hotline.Core.Activation;

/// <summary>A Copilot-key (or hotkey) state change.</summary>
public enum KeyEvent { Tap, HoldStart, HoldStop }

/// <summary>Which channel delivered the key event (for logging and de-duplication diagnostics).</summary>
public enum KeySource { Protocol, FastPath, Hotkey }

/// <summary>What the app should do in response to a key event. Configurable per event in settings.</summary>
/// <summary>Voice: hold to dictate into the message, release to stop (as the long press; as the short press it toggles).</summary>
public enum KeyAction { None, TogglePopup, ShowPopup, NewChat, CaptureWindow, RegionSelect, Voice }
