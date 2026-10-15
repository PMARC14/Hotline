using System.Text.RegularExpressions;

namespace Hotline.Core.Chat;

/// <summary>"/resume" (the last chat, like Ctrl+↑) or "/resume words" (the newest chat whose title has every word).</summary>
public static partial class ResumeCommand
{
    public static bool TryParse(string text, out string query)
    {
        var m = Command().Match(text.Trim());
        query = m.Success ? m.Groups[1].Value.Trim() : "";
        return m.Success;
    }

    /// <summary>The newest chat other than <paramref name="currentId"/> matching <paramref name="query"/>, or null.</summary>
    public static HistoryStore.Summary? Pick(IEnumerable<HistoryStore.Summary> newestFirst, string? currentId, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return newestFirst.FirstOrDefault(c => c.Id != currentId
            && words.All(w => c.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase)));
    }

    [GeneratedRegex(@"^/resume(?:\s+(.*))?\z", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Command();
}
