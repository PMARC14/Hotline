namespace Hotline.Core.Activation;

/// <summary>Subset of activation kinds Hotline cares about (mapped from Windows App SDK's ExtendedActivationKind).</summary>
public enum ActivationKind { Launch, Protocol, ProtocolForResults, StartupTask, Other }

/// <summary>
/// Plain snapshot of an activation. Windows activation args for a redirected launch are proxies into the
/// short-lived redirecting process; read them once, immediately, and work from this copy.
/// </summary>
public sealed record ActivationRequest(ActivationKind Kind, Uri? Uri, bool IsFirstLaunch);

/// <summary>What to do: route a key event, show the popup, or (neither) stay in the tray.</summary>
public sealed record ActivationPlan(KeyEvent? Key, bool ShowPopup);

public static class ActivationPlanner
{
    public static ActivationPlan Plan(ActivationRequest r) => r.Kind switch
    {
        // A Copilot key press while Hotline isn't running arrives as ProtocolForResults.
        ActivationKind.Protocol or ActivationKind.ProtocolForResults =>
            ActivationParser.ParseUri(r.Uri) is { } key ? new ActivationPlan(key, false)
            // hotline://tray: start or wake quietly in the tray (used by install/test scripts)
            : string.Equals(r.Uri?.Host, "tray", StringComparison.OrdinalIgnoreCase) ? new ActivationPlan(null, false)
            : new ActivationPlan(null, true),
        ActivationKind.StartupTask when r.IsFirstLaunch => new ActivationPlan(null, false), // signed in: stay in tray
        _ => new ActivationPlan(null, true),
    };
}
