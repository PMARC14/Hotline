using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;

namespace Hotline.App.Interop;

/// <summary>
/// The optional fallback hotkey (<c>activation.fallbackHotkey</c>) on the panel window. One WM_HOTKEY handler for the app's
/// lifetime; <see cref="Apply"/> registers the current setting and re-registers it when settings change. Call on the
/// window's thread.
/// </summary>
internal sealed class HotkeyRegistration
{
    private const int HotkeyId = 0x484C; // "HL"
    private readonly WindowMessageHook _hook;
    private readonly FileLog _log;
    private Hotkey? _registered;
    private string? _appliedText; // the setting's text last applied (null before the first Apply)

    public HotkeyRegistration(WindowMessageHook hook, Action onPressed, FileLog log)
    {
        (_hook, _log) = (hook, log);
        hook.On(Native.WM_HOTKEY, (wParam, _) =>
        {
            if (wParam != HotkeyId) return false;
            onPressed();
            return true;
        });
    }

    /// <summary>True when no app holds <paramref name="hotkey"/> right now (registers it on <paramref name="hwnd"/> for a moment).</summary>
    public static bool IsFree(nint hwnd, Hotkey hotkey)
    {
        const int ProbeId = 0x484D;
        if (!Native.RegisterHotKey(hwnd, ProbeId, (uint)hotkey.Modifiers | Native.MOD_NOREPEAT, hotkey.VirtualKey)) return false;
        Native.UnregisterHotKey(hwnd, ProbeId);
        return true;
    }

    /// <summary>
    /// Makes the registered hotkey match <paramref name="text"/>: unchanged when it already does; otherwise the old one is
    /// released and the new one registered. Invalid or already-taken hotkeys are logged and skipped (a later Apply retries).
    /// </summary>
    public void Apply(string? text)
    {
        // Settings change for many reasons: act only when this setting's text changed (an invalid or taken hotkey is then
        // reported once, not on every theme or model change; editing the text, or a restart, tries again).
        var normalized = text?.Trim() ?? "";
        if (_appliedText is not null && string.Equals(normalized, _appliedText, StringComparison.OrdinalIgnoreCase)) return;
        _appliedText = normalized;
        Hotkey? wanted = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (Hotkey.TryParse(text, out var parsed)) wanted = parsed;
            else _log.Error($"fallback hotkey '{text}' is not valid (example: Ctrl+Alt+H); ignored");
        }
        if (Equals(wanted, _registered)) return;
        if (_registered is { } old)
        {
            Native.UnregisterHotKey(_hook.Hwnd, HotkeyId);
            _registered = null;
            _log.Info($"fallback hotkey {old} released");
        }
        if (wanted is not { } hk) return;
        if (!Native.RegisterHotKey(_hook.Hwnd, HotkeyId, (uint)hk.Modifiers | Native.MOD_NOREPEAT, hk.VirtualKey))
        {
            _log.Error($"fallback hotkey {hk} is already used by another app; ignored");
            return;
        }
        _registered = hk;
        _log.Info($"fallback hotkey {hk} registered");
    }
}
