using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Hotline.App;

/// <summary>Turns activations and key events into popup actions. All calls must be on the UI thread.</summary>
public sealed class ActivationRouter(PopupWindow popup, ActivationSettings settings, KeyEventDeduper deduper, FileLog log)
{
    public void OnActivation(AppActivationArguments args, bool isFirstLaunch)
    {
        log.Info($"activation kind={args.Kind} first={isFirstLaunch}");
        switch (args.Kind)
        {
            // A Copilot key press while Hotline isn't running arrives as ProtocolForResults.
            case ExtendedActivationKind.Protocol or ExtendedActivationKind.ProtocolForResults
                when args.Data is IProtocolActivatedEventArgs protocol:
                if (ActivationParser.ParseUri(protocol.Uri) is { } e) OnKey(e, KeySource.Protocol);
                else popup.ShowPopup();
                break;
            case ExtendedActivationKind.StartupTask when isFirstLaunch:
                break; // signed in: stay quietly in the tray
            default:
                popup.ShowPopup();
                break;
        }
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
                popup.ResetConversation();
                popup.ShowPopup();
                break;
            default:
                // ShowPopup, plus CaptureWindow/RegionSelect which arrive in Plan 4 (context); until then they just open the popup.
                popup.ShowPopup();
                break;
        }
    }
}
