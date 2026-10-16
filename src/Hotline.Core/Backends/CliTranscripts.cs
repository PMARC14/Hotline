using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hotline.Core.Chat;
using Hotline.Core.Settings;
using Microsoft.Data.Sqlite;

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

    // ---- Claude Code: ~/.claude/projects/<folder>/<session>.jsonl -----------------------------------------------

    private static List<HistoryStore.Entry> ReadClaude(string file, string backendId)
    {
        var entries = new List<HistoryStore.Entry>();
        foreach (var line in ReadLines(file))
        {
            if (!line.Contains("\"user\"", StringComparison.Ordinal) && !line.Contains("\"assistant\"", StringComparison.Ordinal)) continue; // fast skip
            using var doc = Parse(line);
            if (doc?.RootElement is not { ValueKind: JsonValueKind.Object } root) continue;
            var type = Str(root, "type");
            if (type is not ("user" or "assistant") || Bool(root, "isMeta") || Bool(root, "isSidechain")) continue;
            if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out var content)) continue;
            string text;
            if (content.ValueKind == JsonValueKind.String) text = content.GetString() ?? "";
            else if (content.ValueKind == JsonValueKind.Array)
            {
                // Tool results and thinking aren't conversation; text blocks are.
                if (type == "user" && content.EnumerateArray().Any(b => Str(b, "type") == "tool_result")) continue;
                text = string.Join("\n", content.EnumerateArray().Select(b => Str(b, "type") switch
                {
                    "text" => Str(b, "text") ?? "",
                    "image" => "[attached: image]",
                    _ => "",
                }).Where(s => s.Length > 0));
            }
            else continue;
            Add(entries, type == "user" ? ChatRole.User : ChatRole.Assistant, text, Time(root, "timestamp"), backendId);
        }
        return entries;
    }

    // ---- Codex: ~/.codex/sessions/YYYY/MM/DD/rollout-<time>-<thread>.jsonl ------------------------------------

    private static List<HistoryStore.Entry> ReadCodex(string file, string backendId)
    {
        var entries = new List<HistoryStore.Entry>();
        foreach (var line in ReadLines(file))
        {
            using var doc = Parse(line);
            if (doc?.RootElement is not { ValueKind: JsonValueKind.Object } root || Str(root, "type") != "response_item"
                || !root.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object) continue;
            if (Str(p, "type") != "message" || Str(p, "role") is not ("user" or "assistant") || !p.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array) continue;
            var text = string.Join("\n", content.EnumerateArray().Select(b => Str(b, "type") switch
            {
                "input_text" or "output_text" => Str(b, "text") ?? "",
                "input_image" => "[attached: image]",
                _ => "",
            }).Where(s => s.Length > 0));
            if (Str(p, "role") == "user" && CodexContext.Any(tag => text.TrimStart().StartsWith(tag, StringComparison.Ordinal))) continue;
            Add(entries, Str(p, "role") == "user" ? ChatRole.User : ChatRole.Assistant, text, Time(root, "timestamp"), backendId);
        }
        return entries;
    }

    // ---- agy: ~/.gemini/antigravity-cli/conversations/<id>.db ---------------------------------------------------

    private const int AgyUserStep = 14, AgyAnswerStep = 15;

    /// <summary>
    /// agy keeps each step as a protobuf message in SQLite (steps.step_payload). Read without its schema: a user step
    /// (type 14) holds the text in field 19 › 2, an answer (type 15) in field 20 › 1. Anything else is skipped.
    /// </summary>
    private static List<HistoryStore.Entry>? ReadAgy(string file, string backendId)
    {
        if (!File.Exists(file)) return null;
        var at = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
        var entries = new List<HistoryStore.Entry>();
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1 }.ToString());
        db.Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT step_type, step_payload FROM steps ORDER BY idx";
        using var reader = query.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetValue(0) is not long stepType || reader.GetValue(1) is not byte[] payload) continue; // NULL or odd values
            string? text;
            try
            {
                text = stepType switch
                {
                    AgyUserStep => Proto(payload, 19) is { } user ? ProtoString(user, 2) : null,
                    AgyAnswerStep => Proto(payload, 20) is { } answer ? ProtoString(answer, 1) : null,
                    _ => null,
                };
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or FormatException) { continue; } // a malformed step: skip it
            if (text is not null) Add(entries, stepType == AgyUserStep ? ChatRole.User : ChatRole.Assistant, text, at, backendId);
        }
        return entries;
    }

    /// <summary>The first length-delimited field <paramref name="number"/> of a protobuf message, or null.</summary>
    private static byte[]? Proto(byte[] message, int number)
    {
        var i = 0;
        while (i < message.Length)
        {
            var key = Varint(message, ref i);
            var (field, wire) = ((int)(key >> 3), (int)(key & 7));
            switch (wire)
            {
                case 0: Varint(message, ref i); break;
                case 1: if (message.Length - i < 8) return null; i += 8; break;
                case 5: if (message.Length - i < 4) return null; i += 4; break;
                case 2:
                    var length = Varint(message, ref i);
                    if (length > (ulong)(message.Length - i)) return null; // past the end (compared without int overflow)
                    if (field == number) return message[i..(i + (int)length)];
                    i += (int)length;
                    break;
                default: return null; // groups or garbage: not a message this reader understands
            }
        }
        return null;
    }

    private static string? ProtoString(byte[] message, int number) => Proto(message, number) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;

    private static ulong Varint(byte[] b, ref int i)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var x = b[i++]; // past the end: IndexOutOfRangeException, caught by Read
            value |= (ulong)(x & 0x7F) << shift;
            if (x < 0x80) return value;
        }
        throw new FormatException("varint too long");
    }

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
