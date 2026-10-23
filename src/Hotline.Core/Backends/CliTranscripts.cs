using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

/// <summary>
/// A CLI chat's messages from the CLI's own history, for reopening a chat whose messages Hotline didn't keep
/// (keepInHotline off, the default): Claude Code's session file, Codex's rollout file, agy's conversation database.
/// Read-only; anything missing, unreadable or in a format it doesn't recognise gives null, and the chat opens as before
/// (empty, still resumable). Hotline's own wrapping of what it sent (earlier turns, instructions, attached files) is
/// removed so the messages read as typed (chat.cliTranscripts).
/// </summary>
public static partial class CliTranscripts
{
    public static IReadOnlyList<HistoryStore.Entry>? Read(BackendType type, string sessionId, string home, string backendId)
    {
        if (!SafeId().IsMatch(sessionId)) return null; // an id is only ever a file name, never a path
        try
        {
            var list = type switch
            {
                BackendType.ClaudeCode => FirstFile(Path.Combine(home, ".claude", "projects"), sessionId + ".jsonl") is { } f ? ReadClaude(f, backendId) : null,
                BackendType.Codex => FirstFile(Path.Combine(home, ".codex", "sessions"), "rollout-*-" + sessionId + ".jsonl") is { } f ? ReadCodex(f, backendId) : null,
                BackendType.Antigravity => ReadAgy(Path.Combine(home, ".gemini", "antigravity-cli", "conversations", sessionId + ".db"), backendId),
                _ => null,
            };
            return list is { Count: > 0 } ? list : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) // anything unreadable: the chat opens as before
        {
            return null;
        }
    }

    private static string? FirstFile(string root, string pattern) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, pattern, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).FirstOrDefault()
            : null;

    // ---- common -------------------------------------------------------------------------------------------------

    /// <summary>Adds a message (unwrapped), merging an answer split across several records into one.</summary>
    private static void Add(List<HistoryStore.Entry> entries, ChatRole role, string raw, DateTimeOffset at, string backendId)
    {
        var text = role == ChatRole.User ? Unwrap(raw) : raw.Trim();
        if (text.Length == 0 && role == ChatRole.User && raw.Trim().Length > 0) text = "[attachment]"; // attachments only: keep its place
        if (text.Length == 0) return;
        if (role == ChatRole.Assistant && entries.Count > 0 && entries[^1].Role == ChatRole.Assistant)
        {
            entries[^1] = entries[^1] with { Text = entries[^1].Text + "\n\n" + text };
            return;
        }
        entries.Add(new HistoryStore.Entry($"cli-{entries.Count}", role, text, at, backendId, []));
    }

    /// <summary>
    /// What the user typed, from what Hotline sent: earlier turns and instructions (on a fresh session) are dropped,
    /// attached files and images become "[attached: name]" lines, and the "look at the attachment" stand-in is empty.
    /// </summary>
    public static string Unwrap(string sent)
    {
        var text = sent.ReplaceLineEndings("\n");
        var newMessage = text.LastIndexOf("\nNew message:\n", StringComparison.Ordinal);
        if (newMessage >= 0) text = text[(newMessage + "\nNew message:\n".Length)..];
        else if (text.StartsWith("Instructions for this conversation:\n", StringComparison.Ordinal))
        {
            var end = text.IndexOf("\n\n", StringComparison.Ordinal);
            text = end < 0 ? "" : text[(end + 2)..];
        }
        var attached = new List<string>();
        foreach (Match m in AttachedImages().Matches(text))
            attached.AddRange(m.Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => Path.GetFileName(l.TrimStart('-', ' ').Trim())));
        text = AttachedImages().Replace(text, "");
        foreach (Match m in AttachedFile().Matches(text)) attached.Add(m.Groups[1].Value);
        text = AttachedFile().Replace(text, "").Trim();
        // Hotline's stand-in for "attachments only" isn't something the user typed.
        text = string.Join("\n", text.Split('\n').Where(l => l.Trim() is not ("Please look at the attached file(s)." or "Please look at the attachment(s)."))).Trim();
        return string.Join("\n", new[] { text }.Concat(attached.Select(a => $"[attached: {a}]")).Where(s => s.Length > 0));
    }

    private static IEnumerable<string> ReadLines(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); // the CLI may be writing it
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) yield return line;
    }

    /// <summary>One line's JSON, or null; the caller disposes it (no copy of large lines).</summary>
    private static JsonDocument? Parse(string line)
    {
        try { return JsonDocument.Parse(line); }
        catch (JsonException) { return null; }
    }

    /// <summary>Turns Codex adds itself (not the user's words).</summary>
    private static readonly string[] CodexContext = ["<environment_context>", "<user_instructions>", "<permissions instructions>"];

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static DateTimeOffset Time(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
            ? t : DateTimeOffset.UnixEpoch;

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,128}$")]
    private static partial Regex SafeId();

    [GeneratedRegex(@"Attached images \([^\n]*\):\n((?:- [^\n]*\n?)+)\n?")]
    private static partial Regex AttachedImages();

    [GeneratedRegex(@"Attached file ([^\n]+):\n```\n[\s\S]*?\n```\n\n?")]
    private static partial Regex AttachedFile();
}
