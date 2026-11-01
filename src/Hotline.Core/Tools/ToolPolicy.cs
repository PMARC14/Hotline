namespace Hotline.Core.Tools;

public static class ToolPolicy
{
    /// <summary>
    /// A deny (for the tool or for "server/*") always wins; otherwise the specific rule, then "server/*", then the
    /// default: read-only tools run, others ask.
    /// </summary>
    public static ToolApproval Decide(IReadOnlyDictionary<string, ToolApproval> approvals, string server, string tool, bool readOnly)
    {
        var hasSpecific = approvals.TryGetValue($"{server}/{tool}", out var specific);
        var hasWildcard = approvals.TryGetValue($"{server}/*", out var wildcard);
        if (hasSpecific && specific == ToolApproval.Deny || hasWildcard && wildcard == ToolApproval.Deny) return ToolApproval.Deny;
        if (hasSpecific) return specific;
        if (hasWildcard) return wildcard;
        return readOnly ? ToolApproval.Allow : ToolApproval.Ask;
    }
}

public static class ToolNames
{
    /// <summary>Names with '/' or '*' would make approval keys ("server/tool", "server/*") ambiguous.</summary>
    public static bool IsAmbiguous(string name) => name.Contains('/') || name.Contains('*');

    /// <summary>
    /// A function name every API accepts (^[A-Za-z_][A-Za-z0-9_-]{0,63}$ — Gemini needs a letter or "_" first), unique
    /// within <paramref name="used"/>.
    /// </summary>
    public static string ForApi(string server, string tool, HashSet<string> used)
    {
        static string Clean(string s) => new(s.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
        var baseName = $"{Clean(server)}__{Clean(tool)}";
        if (!char.IsAsciiLetter(baseName[0]) && baseName[0] != '_') baseName = "_" + baseName;
        if (baseName.Length > 60) baseName = baseName[..60];
        var name = baseName;
        for (var i = 2; !used.Add(name); i++) name = $"{baseName[..Math.Min(baseName.Length, 60 - i.ToString().Length)]}_{i}";
        return name;
    }
}
