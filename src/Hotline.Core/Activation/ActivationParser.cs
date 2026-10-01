namespace Hotline.Core.Activation;

public static class ActivationParser
{
    public const string Scheme = "hotline";

    /// <summary>Parses hotline://key?state=Tap|Down|Up (case-insensitive). Returns null if not a key URI.</summary>
    public static KeyEvent? ParseUri(Uri? uri)
    {
        if (uri is null || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
            return null;

        return GetQueryValue(uri.Query, "state")?.ToLowerInvariant() switch
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

    private static string? GetQueryValue(string query, string name)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && string.Equals(Uri.UnescapeDataString(kv[0]), name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }
}
