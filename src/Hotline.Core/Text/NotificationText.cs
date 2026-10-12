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
        s = Control().Replace(s, " ");
        s = Space().Replace(s, " ").Trim();
        if (s.Length <= max) return s;
        var cut = char.IsHighSurrogate(s[max - 1]) ? max - 1 : max; // don't split an emoji (the XML would be invalid)
        return s[..cut].TrimEnd() + "…";
    }

    [GeneratedRegex(@"```.*?(```|$)", RegexOptions.Singleline)] private static partial Regex CodeBlock();
    [GeneratedRegex(@"\$\$.*?(\$\$|$)", RegexOptions.Singleline)] private static partial Regex MathBlock();
    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")] private static partial Regex Link();
    // Emphasis underscores only at word edges, so snake_case names survive.
    [GeneratedRegex(@"(?m)^\s{0,3}(#{1,6}\s+|>\s?|[-*+]\s+|\d+[.)]\s+|\|)|[*`~|]|(?<!\w)_+|_+(?!\w)")] private static partial Regex Marks();
    // Not allowed in XML (toast content), and terminal escapes from CLI errors: ESC [ ... letter.
    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]|[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")] private static partial Regex Control();
    [GeneratedRegex(@"\s+")] private static partial Regex Space();
}
