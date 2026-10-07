using System.Text.RegularExpressions;

namespace Hotline.Core.Activation;

/// <summary>
/// An App Action launch (Click to Do, the Windows action catalog, or any app): hotline-action://ask or
/// hotline-action://run/&lt;quick action&gt;, with the content as inputs ("text", "image") or, for a plain launch, in
/// the query. Quick actions run at once only when Windows itself is the caller — any app can launch a URI.
/// </summary>
public sealed partial record AppActionRequest(string? QuickAction, string? Text, string? ImagePath, bool FromWindows)
{
    public const string Scheme = "hotline-action";
    public const int MaxTextChars = 50_000;

    /// <summary>Packages that are part of Windows end with this publisher id.</summary>
    private const string WindowsPublisherId = "_cw5n1h2txyewy";

    public bool AutoSend => QuickAction is not null && FromWindows && (Text is not null || ImagePath is not null);

    public static AppActionRequest? Parse(Uri? uri, IReadOnlyDictionary<string, string>? inputs, string? callerPackageFamily)
    {
        if (uri is null || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)) return null;
        string? quickAction = null;
        if (string.Equals(uri.Host, "run", StringComparison.OrdinalIgnoreCase))
        {
            var name = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
            if (!SafeName().IsMatch(name)) return null;
            quickAction = name;
        }
        else if (!string.Equals(uri.Host, "ask", StringComparison.OrdinalIgnoreCase)) return null;

        var values = new Dictionary<string, string>(inputs ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in Query(uri.Query)) values.TryAdd(key, value);
        var text = values.TryGetValue("text", out var t) && !string.IsNullOrWhiteSpace(t) ? (t.Length > MaxTextChars ? t[..MaxTextChars] : t) : null;
        var image = values.TryGetValue("image", out var i) && IsLocalImage(i) ? i : null;
        if (values.ContainsKey("image") && image is null) return null; // an image we won't open: ignore the launch
        if (text is null && image is null) return null;
        var fromWindows = callerPackageFamily?.EndsWith(WindowsPublisherId, StringComparison.OrdinalIgnoreCase) == true;
        return new AppActionRequest(quickAction, text, image, fromWindows);
    }

    /// <summary>Only a local, absolute .png/.jpg/.jpeg path (no network shares, nothing executable).</summary>
    private static bool IsLocalImage(string path) =>
        Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal)
        && Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";

    private static IEnumerable<(string, string)> Query(string query)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            yield return (Uri.UnescapeDataString(pair[..eq]), Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')));
        }
    }

    [GeneratedRegex(@"^[\w\-]+\z")]
    private static partial Regex SafeName();
}
