namespace Hotline.Core.Chat;

/// <summary>Mode the voice input was started with.</summary>
public enum VoiceMode
{
    Hold,
    Tap,
}

/// <summary>Why voice input was stopped automatically by policy.</summary>
public enum VoiceStopReason
{
    None,
    Deactivated,
    SilenceTimeout,
}

/// <summary>
/// Timing and deactivation policy for voice input: tap mode stops when the panel loses activation/focus
/// or after ~30 s of silence without recognized speech; hold-to-talk stops only when the key is released.
/// </summary>
public static class VoiceStopPolicy
{
    public const double TapSilenceTimeoutSeconds = 30.0;
    public static readonly TimeSpan TapSilenceTimeout = TimeSpan.FromSeconds(TapSilenceTimeoutSeconds);

    public static VoiceStopReason Decide(VoiceMode mode, double secondsSinceSpeech, bool panelActive)
    {
        if (mode != VoiceMode.Tap) return VoiceStopReason.None;
        if (!panelActive) return VoiceStopReason.Deactivated;
        if (secondsSinceSpeech >= TapSilenceTimeoutSeconds) return VoiceStopReason.SilenceTimeout;
        return VoiceStopReason.None;
    }

    public static VoiceStopReason Decide(VoiceMode mode, TimeSpan silence, bool panelActive) =>
        Decide(mode, silence.TotalSeconds, panelActive);

    public static bool ShouldStop(VoiceMode mode, double secondsSinceSpeech, bool panelActive) =>
        Decide(mode, secondsSinceSpeech, panelActive) != VoiceStopReason.None;

    public static bool ShouldStop(VoiceMode mode, TimeSpan silence, bool panelActive) =>
        Decide(mode, silence.TotalSeconds, panelActive) != VoiceStopReason.None;

    public static bool ShouldStop(VoiceMode mode, bool panelActive) =>
        Decide(mode, 0.0, panelActive) != VoiceStopReason.None;
}
