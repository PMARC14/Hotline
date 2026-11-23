using Hotline.Core.Text.Localization;

namespace Hotline.Core.Tools;

/// <summary>Parses the settings <c>tools.approvals</c> map into rule entries.</summary>
public static class ToolRules
{
    public static IReadOnlyDictionary<string, ToolApproval> Parse(
        IReadOnlyDictionary<string, string>? approvals, out IReadOnlyList<string> warnings)
    {
        var rules = new Dictionary<string, ToolApproval>(StringComparer.Ordinal);
        var warnList = new List<string>();
        if (approvals is not null)
        {
            foreach (var (key, rawValue) in approvals)
            {
                var text = rawValue?.Trim().ToLowerInvariant();
                var parsed = text switch
                {
                    "allow" => ToolApproval.Allow,
                    "ask" => ToolApproval.Ask,
                    "deny" => ToolApproval.Deny,
                    _ => ToolApproval.Deny,
                };
                if (text is not ("allow" or "ask" or "deny"))
                    warnList.Add(L.F(Strings.Mcp_BadApproval, key));
                if (!rules.TryGetValue(key, out var existing) || existing != ToolApproval.Deny)
                    rules[key] = parsed;
            }
        }
        warnings = warnList;
        return rules;
    }
}
