namespace Hotline.Core.Chat;

/// <summary>
/// The streaming cursor turns into a spinner while the model is working without writing (before the first words, or
/// during a pause: reasoning, a tool call), so a slow answer doesn't look stalled.
/// </summary>
public static class ThinkingIndicator
{
    /// <param name="sinceTextMs">Milliseconds since the answer started or last got text.</param>
    public static bool Show(bool enabled, bool streaming, long sinceTextMs, int delayMs) =>
        enabled && streaming && sinceTextMs >= delayMs;
}
