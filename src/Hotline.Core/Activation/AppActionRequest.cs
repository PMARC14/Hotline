using System.Text.RegularExpressions;
using Hotline.Core.Platform;

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
        var fromWindows = IsWindowsCaller(callerPackageFamily);
        // A path in the link would let any web page attach (or point Windows at) a file: images come only from the
        // inputs Windows itself hands over.
        var image = inputs is not null && inputs.TryGetValue("image", out var i) ? i : null;
        var wantedImage = image is not null || values.ContainsKey("image");
        if (!fromWindows || image is null || !IsSafeLocalImagePath(image)) image = null;
        if (wantedImage && image is null) return null; // an image we won't open: ignore the launch
        if (text is null && image is null) return null;
        return new AppActionRequest(quickAction, text, image, fromWindows);
    }

    public static bool IsWindowsCaller(string? callerPackageFamily) =>
        callerPackageFamily?.EndsWith(WindowsPublisherId, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Only a .png/.jpg/.jpeg on a local drive letter: no network or device paths (in any mix of slashes), no
    /// relative paths, nothing executable. Checked before the file is touched.
    /// </summary>
    public static bool IsSafeLocalImagePath(string? path)
    {
        if (path is null || !SafeDirectory.IsLocal(path) || Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg")) return false;
        try { return new DriveInfo(path[..1]).DriveType != DriveType.Network; } // a mapped share is a network path too
        catch (ArgumentException) { return false; }
    }

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
