using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;

namespace Hotline.App.Interop;

internal static class HotkeyRegistration
{
    private const int HotkeyId = 0x484C; // "HL"

    /// <summary>Registers the optional fallback hotkey. Invalid or already-taken hotkeys are logged and skipped.</summary>
    public static bool TryRegister(WindowMessageHook hook, string? text, Action onPressed, FileLog log)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (!Hotkey.TryParse(text, out var hk))
        {
            log.Error($"fallback hotkey '{text}' is not valid (example: Ctrl+Alt+H); ignored");
            return false;
        }
        if (!Native.RegisterHotKey(hook.Hwnd, HotkeyId, (uint)hk.Modifiers | Native.MOD_NOREPEAT, hk.VirtualKey))
        {
            log.Error($"fallback hotkey {hk} is already used by another app; ignored");
            return false;
        }
        hook.On(Native.WM_HOTKEY, (wParam, _) =>
        {
            if (wParam != HotkeyId) return false;
            onPressed();
            return true;
        });
        log.Info($"fallback hotkey {hk} registered");
        return true;
    }
}
