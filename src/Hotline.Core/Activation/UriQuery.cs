namespace Hotline.Core.Activation;

/// <summary>The one reader of a link's query string (<c>?a=1&amp;b=x+y</c>): percent escapes decoded, '+' read as a space.</summary>
internal static class UriQuery
{
    /// <summary>The name/value pairs in order; parts without a name or an '=' are skipped.</summary>
    public static IEnumerable<(string Name, string Value)> Pairs(string query)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            yield return (Uri.UnescapeDataString(pair[..eq].Replace('+', ' ')), Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')));
        }
    }

    /// <summary>The first value of <paramref name="name"/> (case-insensitive), or null.</summary>
    public static string? Get(string query, string name)
    {
        foreach (var (key, value) in Pairs(query))
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return value;
        return null;
    }
}
