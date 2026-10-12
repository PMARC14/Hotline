using System.Text.RegularExpressions;

namespace Hotline.Core.Text;

/// <summary>A short plain-text preview of an answer for a Windows notification (no Markdown, code or formulas).</summary>
public static partial class NotificationText
{
    public static string Snippet(string markdown, int max = 140)
    {
        var s = CodeBlock().Replace(markdown ?? "", " ");
        s = MathBlock().Replace(s, " ");
        s = Link().Replace(s, "$1");
        s = Marks().Replace(s, "");
        s = Space().Replace(s, " ").Trim();
        return s.Length <= max ? s : s[..max].TrimEnd() + "…";
    }

    [GeneratedRegex(@"```.*?(```|$)", RegexOptions.Singleline)] private static partial Regex CodeBlock();
    [GeneratedRegex(@"\$\$.*?(\$\$|$)", RegexOptions.Singleline)] private static partial Regex MathBlock();
    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")] private static partial Regex Link();
    [GeneratedRegex(@"(?m)^\s{0,3}(#{1,6}\s+|>\s?|[-*+]\s+|\d+[.)]\s+|\|)|[*_`~|]")] private static partial Regex Marks();
    [GeneratedRegex(@"\s+")] private static partial Regex Space();
}
