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
        if (!string.Equals(uri?.Scheme, AppActionRequest.Scheme, StringComparison.OrdinalIgnoreCase))
            return new ActivationRequest(kind, uri, isFirstLaunch);

        // App Actions: copy the inputs and the caller now, and tell Windows we're done (we return no results; the
        // panel opens with the content). Waiting would keep Click to Do's request open.
        Dictionary<string, string>? inputs = null;
        string? caller = null;
        switch (args.Data)
        {
            case ProtocolForResultsActivatedEventArgs forResults:
                inputs = Copy(forResults.Data);
                caller = forResults.CallerPackageFamilyName;
                KeepImage(inputs); // a temporary photo may be deleted once we report completion
                forResults.ProtocolForResultsOperation?.ReportCompleted(new Windows.Foundation.Collections.ValueSet());
                break;
            case IProtocolActivatedEventArgsWithCallerPackageFamilyNameAndData withData:
                inputs = Copy(withData.Data);
                caller = withData.CallerPackageFamilyName;
                break;
        }
        return new ActivationRequest(kind, uri, isFirstLaunch, inputs, caller);
    }

    /// <summary>Folder for images handed over by App Actions (deleted after they're attached).</summary>
    public static string ActionImagesFolder => Path.Combine(Path.GetTempPath(), "hotline-actions");

    /// <summary>Copies an action's image (png/jpg, at most 20 MB) to <see cref="ActionImagesFolder"/> and points the input there.</summary>
    private static void KeepImage(Dictionary<string, string>? inputs)
    {
        if (inputs is null || !inputs.TryGetValue("image", out var path)) return;
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg") || !File.Exists(path) || new FileInfo(path).Length > 20 * 1024 * 1024) return;
            Directory.CreateDirectory(ActionImagesFolder);
            var copy = Path.Combine(ActionImagesFolder, Guid.NewGuid().ToString("N") + ext);
            File.Copy(path, copy);
            inputs["image"] = copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { } // keep the original path
    }

    private static Dictionary<string, string>? Copy(Windows.Foundation.Collections.ValueSet? data) =>
        data?.Where(kv => kv.Value is string).ToDictionary(kv => kv.Key, kv => (string)kv.Value);

    /// <summary>An App Action (Click to Do, another app): open the panel with its content and maybe run it.</summary>
    public event Action<AppActionRequest>? AppActionRequested;

    /// <summary>hotline://selftest renders a scripted conversation in the hidden panel (no typing).</summary>
    public event Action<Uri?>? SelfTestRequested;
    /// <summary>hotline://demo shows the panel with a scripted example conversation (README screenshots; no AI call).</summary>
    public event Action? DemoRequested;
    public event Action? OpenSettingsRequested;
    /// <summary>The Voice key action: start (hold), stop (release) or toggle (short press).</summary>
    public event Action<VoiceCommand>? VoiceRequested;

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
        if (plan.AppAction is { } action)
        {
            log.Info($"app action: {(action.QuickAction ?? "ask")} ({(action.Text is null ? "image" : $"{action.Text.Length} chars")}, from Windows: {action.FromWindows})");
            popup.ShowPopup(readSelection: false); // the action brings its own content
            AppActionRequested?.Invoke(action);
            return;
        }
        if (plan.Key is { } key) OnKey(key, KeySource.Protocol);
        else if (plan.ShowPopup) popup.ShowPopup();
    }

    public void OnKey(KeyEvent e, KeySource source)
    {
        if (settings.CopilotKey == CopilotKeyMode.RightCtrl && source != KeySource.Hotkey)
        {
            log.Info($"key {e} via {source} ignored (the Copilot key acts as Right Ctrl)");
            return;
        }
        if (!deduper.ShouldHandle(e, source))
        {
            log.Info($"key {e} via {source} (duplicate, ignored)");
            return;
        }
        var action = KeyActionResolver.Resolve(e, settings);
        log.Info($"key {e} via {source} -> {action}");
        popup.SetStatus($"Last key: {e} via {source} → {action}");
        Execute(action, e);
    }

    public void TogglePopup() => popup.Toggle();

    private void Execute(KeyAction action, KeyEvent e = KeyEvent.Tap)
    {
        switch (action)
        {
            case KeyAction.Voice when e == KeyEvent.HoldStop:
                VoiceRequested?.Invoke(VoiceCommand.Stop);
                break;
            case KeyAction.Voice:
                popup.ShowPopup();
                VoiceRequested?.Invoke(e == KeyEvent.HoldStart ? VoiceCommand.Start : VoiceCommand.Toggle);
                break;
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
            case KeyAction.RegionSelect:
                popup.ShowPopup();
                popup.RequestRegionCapture();
                break;
            default:
                popup.ShowPopup();
                break;
        }
    }
}

public enum VoiceCommand { Start, Stop, Toggle }
