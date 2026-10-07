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

    /// <summary>Larger files aren't actions (keeps typing "/" fast).</summary>
    public const int MaxFileBytes = 64 * 1024;

    private readonly Lock _gate = new();
    private string? _signature;
    private IReadOnlyList<QuickAction> _cached = [];

    /// <summary>
    /// The actions, sorted by name. Only file metadata is checked per call; the files are re-read when any name,
    /// size or time changed, so edits apply on the next use without reading every file on each keystroke.
    /// </summary>
    public IReadOnlyList<QuickAction> List()
    {
        List<FileInfo> files;
        try
        {
            var dir = new DirectoryInfo(directory);
            if (!dir.Exists) return [];
            files = dir.EnumerateFiles("*.md").Where(f => SafeName().IsMatch(Path.GetFileNameWithoutExtension(f.Name)) && f.Length <= MaxFileBytes)
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        var signature = string.Join("|", files.Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc.Ticks}"));
        lock (_gate) if (signature == _signature) return _cached;

        var list = new List<QuickAction>();
        var complete = true;
        foreach (var file in files)
        {
            string text;
            try { text = File.ReadAllText(file.FullName).Trim(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { complete = false; continue; } // e.g. an editor saving it
            if (text.Length == 0) continue;
            var first = text.Split('\n', 2)[0].Trim();
            list.Add(new QuickAction(Path.GetFileNameWithoutExtension(file.Name), first, text.ReplaceLineEndings("\n")));
        }
        if (complete) lock (_gate) (_signature, _cached) = (signature, list); // otherwise read again next time
        return list;
    }

    public string PathFor(string name) => Path.Combine(directory, name + ".md");

    /// <summary>A new action file with a template instruction (Settings › Quick actions); returns its name.</summary>
    public string Create()
    {
        System.IO.Directory.CreateDirectory(directory);
        var name = "my-action";
        for (var i = 2; File.Exists(PathFor(name)); i++) name = $"my-action-{i}";
        File.WriteAllText(PathFor(name), "Describe what to do with the text below, e.g. \"Rewrite it as a short, friendly email.\"\n");
        return name;
    }

    public bool Delete(string name)
    {
        if (!SafeName().IsMatch(name) || !File.Exists(PathFor(name))) return false;
        File.Delete(PathFor(name));
        return true;
    }

    /// <summary>Commands Hotline handles itself (listed with the actions; a file with the same name is ignored).</summary>
    public static readonly IReadOnlyList<QuickAction> BuiltIns =
    [
        new("remember", "Save a note to memory.md — every chat sees it", ""),
    ];

    public IReadOnlyList<QuickAction> Matching(string query) =>
        BuiltIns.Concat(List().Where(a => !BuiltIns.Any(b => b.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase))))
            .Where(a => a.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)).ToList();

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
        if (BuiltIns.Any(b => b.Name.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase))) return null; // handled by Hotline
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
