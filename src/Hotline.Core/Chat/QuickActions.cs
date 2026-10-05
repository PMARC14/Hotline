using System.Text.RegularExpressions;

namespace Hotline.Core.Chat;

/// <summary>A quick action: <c>/name</c> in the composer applies <see cref="Instruction"/> to the message.</summary>
public sealed record QuickAction(string Name, string Description, string Instruction);

/// <summary>
/// Quick actions as Markdown files, %USERPROFILE%\.hotline\actions\&lt;name&gt;.md: the file is the instruction, its
/// first line the description shown in the suggestion list. Read on each use, so new or edited files apply live.
/// </summary>
public sealed partial class QuickActions(string directory)
{
    public const string AttachmentsNote = "Apply this to the attached content.";

    private static readonly (string Name, string Text)[] Defaults =
    [
        ("translate", "Translate the text below into English. If it is already English, or I named a language first (e.g. \"to French: …\"), " +
                      "translate it into that language instead. Keep the formatting. Reply with the translation only."),
        ("summarize", "Summarize the text below in a few bullet points, most important first. Keep names, numbers, dates and decisions."),
        ("fix", "Fix the spelling, grammar and punctuation of the text below. Keep my wording, tone and formatting; change only what is wrong. " +
                "Reply with the corrected text only."),
        ("explain", "Explain the text below in plain language: what it says or does, and anything non-obvious. If it is code, walk through it " +
                    "and point out problems."),
    ];

    public string Directory => directory;

    /// <summary>Writes the default actions the first time (when the folder doesn't exist yet); deleted ones stay deleted.</summary>
    public void EnsureDefaults()
    {
        if (System.IO.Directory.Exists(directory)) return;
        System.IO.Directory.CreateDirectory(directory);
        foreach (var (name, text) in Defaults) File.WriteAllText(Path.Combine(directory, name + ".md"), text + "\n");
    }

    public IReadOnlyList<QuickAction> List()
    {
        if (!System.IO.Directory.Exists(directory)) return [];
        var list = new List<QuickAction>();
        IEnumerable<string> files;
        try { files = System.IO.Directory.EnumerateFiles(directory, "*.md").ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        foreach (var path in files)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!SafeName().IsMatch(name)) continue;
            string text;
            try { text = File.ReadAllText(path).Trim(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (text.Length == 0) continue;
            var first = text.Split('\n', 2)[0].Trim();
            list.Add(new QuickAction(name, first, text.ReplaceLineEndings("\n")));
        }
        return list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<QuickAction> Matching(string query) =>
        List().Where(a => a.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The action name being typed (the text is "/" plus a partial name and nothing else), or null.</summary>
    public static string? SuggestionQuery(string text) =>
        text.StartsWith('/') && SafeNameOrEmpty().IsMatch(text[1..]) ? text[1..] : null;

    /// <summary>
    /// "/name message" → the action's instruction applied to the message. With no message the instruction applies to
    /// the attachments, or Text is null when there are none (nothing to apply it to). Null when the text doesn't start
    /// with a known action.
    /// </summary>
    public (QuickAction Action, string? Text)? Expand(string text, bool hasAttachments)
    {
        var m = Command().Match(text);
        if (!m.Success) return null;
        var action = List().FirstOrDefault(a => string.Equals(a.Name, m.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        if (action is null) return null;
        var rest = text[m.Length..].Trim();
        if (rest.Length > 0) return (action, $"{action.Instruction}\n\n---\n\n{rest}");
        return (action, hasAttachments ? $"{action.Instruction}\n\n{AttachmentsNote}" : null);
    }

    [GeneratedRegex(@"^[\w\-]+\z")]
    private static partial Regex SafeName();

    [GeneratedRegex(@"^[\w\-]*\z")]
    private static partial Regex SafeNameOrEmpty();

    [GeneratedRegex(@"^/([\w\-]+)(?=\s|\z)")]
    private static partial Regex Command();
}
