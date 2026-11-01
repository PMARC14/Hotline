namespace Hotline.Core.Activation;

public static class ActivationParser
{
    public const string Scheme = "hotline";

    /// <summary>Parses hotline://key?state=Tap|Down|Up (case-insensitive). Returns null if not a key URI.</summary>
    public static KeyEvent? ParseUri(Uri? uri)
    {
        // The host matters: hotline://settings?state=tap is a settings link, not a key press.
        if (uri is null || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "key", StringComparison.OrdinalIgnoreCase))
            return null;

        return UriQuery.Get(uri.Query, "state")?.ToLowerInvariant() switch
        {
            "tap" => KeyEvent.Tap,
            "down" => KeyEvent.HoldStart,
            "up" => KeyEvent.HoldStop,
            _ => null,
        };
    }

    /// <summary>Maps the MessageWParam values declared in Package.appxmanifest (0/1/2).</summary>
    public static KeyEvent? ParseFastPath(nuint wParam) => (ulong)wParam switch
    {
        0UL => KeyEvent.Tap,
        1UL => KeyEvent.HoldStart,
        2UL => KeyEvent.HoldStop,
        _ => null,
    };
}
