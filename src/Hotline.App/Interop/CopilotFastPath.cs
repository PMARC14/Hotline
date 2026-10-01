using System.Runtime.InteropServices;
using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;

namespace Hotline.App.Interop;

/// <summary>
/// Registers the window for Copilot-key "fast path" invocation: while Hotline is running, the shell
/// sends WM_COPILOT with the manifest's MessageWParam instead of a (slower) protocol launch.
/// See learn.microsoft.com/windows/apps/develop/windows-integration/copilot-key-state.
/// </summary>
internal static class CopilotFastPath
{
    public const uint WM_COPILOT = Native.WM_APP + 1;
    private static readonly Guid FastPathFmtId = new("38652BCA-4329-4E74-86F9-39CF29345EEA");

    public static bool Register(WindowMessageHook hook, Action<KeyEvent> onKey, FileLog log)
    {
        try
        {
            var iid = typeof(Native.IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(Native.SHGetPropertyStoreForWindow(hook.Hwnd, ref iid, out var store));
            var key = new Native.PROPERTYKEY { fmtid = FastPathFmtId, pid = 2 };
            var value = new Native.PROPVARIANT { vt = Native.VT_UINT, uintVal = WM_COPILOT };
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
            Marshal.ThrowExceptionForHR(store.Commit());

            hook.On(WM_COPILOT, (wParam, _) =>
            {
                log.Debug($"fast path message wParam={wParam} foreground=0x{Native.GetForegroundWindow():X} ({Native.ClassNameOf(Native.GetForegroundWindow())})");
                if (ActivationParser.ParseFastPath((nuint)wParam) is { } e) onKey(e);
                else log.Info($"fast path: unknown wParam {wParam}");
                return true;
            });
            log.Info("Copilot key fast path registered");
            return true;
        }
        catch (Exception ex)
        {
            log.Error("Copilot key fast path registration failed (protocol activation still works)", ex);
            return false;
        }
    }
}
