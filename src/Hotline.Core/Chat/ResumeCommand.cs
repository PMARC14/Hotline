using System.Text.RegularExpressions;

namespace Hotline.Core.Chat;

/// <summary>"/resume" opens the history list; "/resume words" opens it filtered to chats whose title has every word.</summary>
public static partial class ResumeCommand
{
    public static bool TryParse(string text, out string query)
    {
        var m = Command().Match(text.Trim());
        query = m.Success ? m.Groups[1].Value.Trim() : "";
        return m.Success;
    }

    /// <summary>
    /// While typing: "/resume" and a space (and any words after it) opens the history list at once, with the words as
    /// its search (chat.resumeWhileTyping). False while the command itself is still being typed.
    /// </summary>
    public static bool TryTyping(string text, out string query)
    {
        var m = Typing().Match(text);
        query = m.Success ? m.Groups[1].Value.Trim() : "";
        return m.Success;
    }

    [GeneratedRegex(@"^/resume(?:\s+(.*))?\z", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Command();

    [GeneratedRegex(@"^/resume[ \t]+([^\r\n]*)\z", RegexOptions.IgnoreCase)]
    private static partial Regex Typing();
}
