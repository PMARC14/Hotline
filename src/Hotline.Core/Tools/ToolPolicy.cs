using Hotline.Core.State;

namespace Hotline.Core.Tools;

public sealed record ToolPolicyDecision(ToolApproval Approval, bool CanAlwaysAllow, string? CaseMismatchAllowRule = null);

/// <summary>
/// Which tools may run without asking, ask in the panel, or stay hidden from the API.
/// Rules (settings tools.approvals) win over grants (state.json), which apply only when no rule matches
/// and the server's launch hash matches. Deny and ask match case-insensitively (broader match is safe);
/// allow matches server and tool case-sensitively (ordinal) and reports case-only matches as a warning.
/// </summary>
public static class ToolPolicy
{
    public static ToolApproval Decide(IReadOnlyDictionary<string, ToolApproval> rules, string server, string tool, bool readOnly) =>
        Evaluate(rules, server, tool, readOnly).Approval;

    public static ToolPolicyDecision Evaluate(
        IReadOnlyDictionary<string, ToolApproval> rules, IGrantStore? grants,
        string server, string tool, bool readOnly, string? launchHash)
    {
        var hasGrant = !string.IsNullOrEmpty(launchHash) && grants?.IsGranted(server, tool, launchHash) == true;
        return Evaluate(rules, server, tool, readOnly, hasGrant);
    }

    public static ToolPolicyDecision Evaluate(
        IReadOnlyDictionary<string, ToolApproval> rules,
        string server, string tool, bool readOnly, bool hasGrant = false)
    {
        var match = InspectRules(rules, server, tool);
        var mismatch = match.MismatchAllowKey;
        if (match.AnyDeny)
            return new ToolPolicyDecision(ToolApproval.Deny, CanAlwaysAllow: false, mismatch);
        if (match.ExactSpecificAsk)
            return new ToolPolicyDecision(ToolApproval.Ask, CanAlwaysAllow: false, mismatch);
        if (match.ExactSpecificAllow)
            return new ToolPolicyDecision(ToolApproval.Allow, CanAlwaysAllow: false, mismatch);
        if (match.AnySpecificAsk)
            return new ToolPolicyDecision(ToolApproval.Ask, CanAlwaysAllow: false, mismatch);
        if (match.AnyWildcardAsk)
            return new ToolPolicyDecision(ToolApproval.Ask, CanAlwaysAllow: false, mismatch);
        if (match.ExactWildcardAllow)
            return new ToolPolicyDecision(ToolApproval.Allow, CanAlwaysAllow: false, mismatch);
        if (hasGrant)
            return new ToolPolicyDecision(ToolApproval.Allow, CanAlwaysAllow: false, mismatch);
        return readOnly
            ? new ToolPolicyDecision(ToolApproval.Allow, CanAlwaysAllow: false, mismatch)
            : new ToolPolicyDecision(ToolApproval.Ask, CanAlwaysAllow: true, mismatch);
    }

    private readonly record struct RuleMatch(
        bool AnyDeny,
        bool ExactSpecificAsk,
        bool AnySpecificAsk,
        bool ExactSpecificAllow,
        bool AnyWildcardAsk,
        bool ExactWildcardAllow,
        string? MismatchAllowKey);

    private static RuleMatch InspectRules(IReadOnlyDictionary<string, ToolApproval> rules, string server, string tool)
    {
        var specificKey = $"{server}/{tool}";
        var wildcardKey = $"{server}/*";
        bool anyDeny = false, exactSpecAsk = false, anySpecAsk = false, exactSpecAllow = false, anyWildAsk = false, exactWildAllow = false;
        string? mismatchAllow = null;

        foreach (var (key, value) in rules)
        {
            if (string.Equals(key, specificKey, StringComparison.OrdinalIgnoreCase))
            {
                var exact = string.Equals(key, specificKey, StringComparison.Ordinal);
                switch (value)
                {
                    case ToolApproval.Deny: anyDeny = true; break;
                    case ToolApproval.Ask: anySpecAsk = true; if (exact) exactSpecAsk = true; break;
                    case ToolApproval.Allow:
                        if (exact) exactSpecAllow = true;
                        else mismatchAllow ??= key;
                        break;
                }
            }
            else if (string.Equals(key, wildcardKey, StringComparison.OrdinalIgnoreCase))
            {
                var exact = string.Equals(key, wildcardKey, StringComparison.Ordinal);
                switch (value)
                {
                    case ToolApproval.Deny: anyDeny = true; break;
                    case ToolApproval.Ask: anyWildAsk = true; break;
                    case ToolApproval.Allow:
                        if (exact) exactWildAllow = true;
                        else mismatchAllow ??= key;
                        break;
                }
            }
        }
        return new RuleMatch(anyDeny, exactSpecAsk, anySpecAsk, exactSpecAllow, anyWildAsk, exactWildAllow, mismatchAllow);
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
