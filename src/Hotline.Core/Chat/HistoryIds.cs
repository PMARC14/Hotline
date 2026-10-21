namespace Hotline.Core.Chat;

/// <summary>Conversation ids name files (<c>&lt;id&gt;.jsonl</c>, handoff files): only plain ids may, so none can escape the folder.</summary>
public static class HistoryIds
{
    public static bool IsSafe(string? id) =>
        id is { Length: >= 1 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
