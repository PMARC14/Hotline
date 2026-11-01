namespace Hotline.Core.Activation;

/// <summary>
/// Which options of <c>hotline://selftest</c> may run. Any web page can open a hotline:// link, so the plain self-test (it only
/// renders into a hidden window and reads Hotline's own settings) always runs, but an option that starts other programs
/// (<c>?tools</c> starts every mcp.json server) or reads other apps (<c>?selection</c> reads the foreground selection) is honoured
/// only when the marker file <c>~/.hotline/.allow-selftest-extras</c> exists, which a web page cannot create.
/// </summary>
public static class SelfTestOptions
{
    public const string MarkerFile = ".allow-selftest-extras";

    /// <summary>Options that need the marker file.</summary>
    public static readonly IReadOnlySet<string> Gated = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "selection", "tools" };

    public static string MarkerPath(string dataDirectory) => Path.Combine(dataDirectory, MarkerFile);

    /// <summary>The option names in a link's query (<c>?selection&amp;voice</c>), lower case, in order, once each.</summary>
    public static IReadOnlyList<string> Requested(Uri? uri)
    {
        var query = uri?.Query.TrimStart('?') ?? "";
        var names = new List<string>();
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(part.Split('=', 2)[0]).Trim().ToLowerInvariant();
            if (name.Length > 0 && !names.Contains(name)) names.Add(name);
        }
        return names;
    }

    /// <summary>The requested options split into the ones that run and the gated ones that are refused (no marker file).</summary>
    public static (IReadOnlyList<string> Allowed, IReadOnlyList<string> Refused) Resolve(Uri? uri, bool extrasAllowed)
    {
        var requested = Requested(uri);
        var refused = extrasAllowed ? [] : requested.Where(Gated.Contains).ToList();
        return ([.. requested.Except(refused)], refused);
    }
}
