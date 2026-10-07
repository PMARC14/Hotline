namespace Hotline.Core.Chat;

/// <summary>Joins dictated text onto the message being written.</summary>
public static class VoiceText
{
    public static string Append(string draft, string dictated)
    {
        var words = dictated.Trim();
        if (words.Length == 0) return draft;
        if (draft.Length == 0) return words;
        // No space before closing punctuation ("hello" + "." → "hello.").
        return char.IsWhiteSpace(draft[^1]) || words[0] is '.' or ',' or '?' or '!' or ';' or ':' or ')' ? draft + words : draft + " " + words;
    }
}
