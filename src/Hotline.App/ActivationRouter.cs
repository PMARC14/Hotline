using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using ActivationKind = Hotline.Core.Activation.ActivationKind;

namespace Hotline.App;

/// <summary>Turns activations and key events into popup actions. All calls except <see cref="Snapshot"/> must be on the UI thread.</summary>
public sealed class ActivationRouter(PopupWindow popup, ActivationSettings settings, KeyEventDeduper deduper, FileLog log)
{
    /// <summary>
    /// Copies everything needed out of the WinRT activation args. Call this synchronously where the args are
    /// received: for a redirected launch they proxy into the redirecting process, which exits once the
    /// redirect completes, after which any access fails with RPC_E_DISCONNECTED/0x800706BE (seen as crashes).
    /// </summary>
    public static ActivationRequest Snapshot(AppActivationArguments args, bool isFirstLaunch)
    {
        var kind = args.Kind switch
        {
            ExtendedActivationKind.Launch => ActivationKind.Launch,
            ExtendedActivationKind.Protocol => ActivationKind.Protocol,
            ExtendedActivationKind.ProtocolForResults => ActivationKind.ProtocolForResults,
            ExtendedActivationKind.StartupTask => ActivationKind.StartupTask,
            _ => ActivationKind.Other,
        };
        var uri = args.Data is IProtocolActivatedEventArgs protocol ? protocol.Uri : null;
        return new ActivationRequest(kind, uri, isFirstLaunch);
    }

    /// <summary>hotline://selftest renders a scripted conversation in the hidden panel (no typing).</summary>
    public event Action<Uri?>? SelfTestRequested;
    /// <summary>hotline://demo shows the panel with a scripted example conversation (README screenshots; no AI call).</summary>
    public event Action? DemoRequested;
    public event Action? OpenSettingsRequested;

    public void OnActivation(ActivationRequest request)
    {
        log.Info($"activation kind={request.Kind} first={request.IsFirstLaunch}");
        log.Debug($"activation uri={request.Uri}");
        if (string.Equals(request.Uri?.Host, "demo", StringComparison.OrdinalIgnoreCase))
        {
            DemoRequested?.Invoke();
            return;
        }
        if (string.Equals(request.Uri?.Host, "selftest", StringComparison.OrdinalIgnoreCase))
        {
            SelfTestRequested?.Invoke(request.Uri);
            return;
        }
        var plan = ActivationPlanner.Plan(request);
        if (plan.OpenSettings) { OpenSettingsRequested?.Invoke(); return; }
        if (plan.Key is { } key) OnKey(key, KeySource.Protocol);
        else if (plan.ShowPopup) popup.ShowPopup();
    }

    public void OnKey(KeyEvent e, KeySource source)
    {
        if (!deduper.ShouldHandle(e, source))
        {
            log.Info($"key {e} via {source} (duplicate, ignored)");
            return;
        }
        var action = KeyActionResolver.Resolve(e, settings);
        log.Info($"key {e} via {source} -> {action}");
        popup.SetStatus($"Last key: {e} via {source} → {action}");
        Execute(action);
    }

    public void TogglePopup() => popup.Toggle();

    private void Execute(KeyAction action)
    {
        switch (action)
        {
            case KeyAction.None:
                break;
            case KeyAction.TogglePopup:
                popup.Toggle();
                break;
            case KeyAction.NewChat:
                popup.RequestNewChat();
                popup.ShowPopup();
                break;
            case KeyAction.CaptureWindow:
                popup.ShowPopup();
                popup.RequestCapture(window: true);
                break;
            default:
                // ShowPopup; RegionSelect arrives in a later plan and just opens the popup for now.
                popup.ShowPopup();
                break;
        }
    }
}
