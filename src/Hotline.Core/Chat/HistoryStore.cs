using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hotline.Core.Chat;

/// <summary>
/// One JSONL file per conversation (the transcript). Attachments are recorded by name/kind only, never their bytes.
/// A chat kept in a CLI's own history also has a small record, &lt;id&gt;.meta.json (title, connection, CLI session and
/// folder), and may have no transcript at all (connection setting keepInHotline off).
/// </summary>
public sealed class HistoryStore(string directory, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private const string MetaSuffix = ".meta.json";

    public sealed record AttachmentInfo(string Name, AttachmentKind Kind, string MimeType);
    public sealed record Entry(string Id, ChatRole Role, string Text, DateTimeOffset At, string? BackendId, IReadOnlyList<AttachmentInfo> Attachments);

    /// <summary>A chat's record: what Recent chats shows and what resuming its CLI conversation needs.</summary>
    public sealed record Meta(string Title, DateTimeOffset LastAt, string? BackendId, string? SessionId, string? WorkingDirectory, int MessageCount);

    /// <summary>One line in the Recent menu. HasTranscript false: only the record (the messages are in the CLI's history).</summary>
    public sealed record Summary(string Id, string Title, DateTimeOffset LastAt, int MessageCount, bool HasTranscript = true, string? BackendId = null);

    /// <summary>The most recently used conversations, newest first. Unreadable or empty files are skipped.</summary>
    public IReadOnlyList<Summary> Recent(int count)
    {
        if (!Directory.Exists(directory)) return [];
        var latest = new Dictionary<string, DateTime>();
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles())
        {
            var id = IdOf(file.Name);
            if (id is null) continue;
            if (!latest.TryGetValue(id, out var at) || file.LastWriteTimeUtc > at) latest[id] = file.LastWriteTimeUtc;
        }
        var result = new List<Summary>();
        foreach (var id in latest.OrderByDescending(kv => kv.Value).Select(kv => kv.Key))
        {
            if (result.Count >= count) break;
            try
            {
                var entries = Load(id);
                var meta = LoadMeta(id);
                if (entries.Count > 0)
                {
                    var first = entries.FirstOrDefault(e => e.Role == ChatRole.User && e.Text.Trim().Length > 0)?.Text ?? entries[0].Text;
                    var title = TitleOf(first);
                    result.Add(new Summary(id, title.Length > 0 ? title : "(attachments)", entries[^1].At, entries.Count, true,
                        meta?.BackendId ?? entries.LastOrDefault(e => e.BackendId is not null)?.BackendId));
                }
                else if (meta is not null)
                    result.Add(new Summary(id, meta.Title.Length > 0 ? meta.Title : "(attachments)", meta.LastAt, meta.MessageCount, false, meta.BackendId));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        }
        return result;
    }

    /// <summary>A chat's title: its first message on one line, at most 60 characters.</summary>
    public static string TitleOf(string text)
    {
        var title = string.Join(' ', text.Split((char[])['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
        return title.Length > 60 ? title[..60].TrimEnd() + "…" : title;
    }

    /// <exception cref="ArgumentException">The id is not a plain id (<see cref="HistoryIds.IsSafe"/>).</exception>
    public string PathFor(string conversationId) => Path.Combine(directory, $"{Checked(conversationId)}.jsonl");

    private string MetaPathFor(string conversationId) => Path.Combine(directory, Checked(conversationId) + MetaSuffix);

    private static string Checked(string id) => HistoryIds.IsSafe(id) ? id : throw new ArgumentException("Not a conversation id.", nameof(id));

    // Ids come back from file names and links: only plain ones count, so a stray file can't name a path.
    private static string? IdOf(string fileName)
    {
        var id = fileName.EndsWith(MetaSuffix, StringComparison.OrdinalIgnoreCase) ? fileName[..^MetaSuffix.Length]
            : fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ? fileName[..^".jsonl".Length]
            : null;
        return HistoryIds.IsSafe(id) ? id : null;
    }

    public void Append(string conversationId, ChatMessage m)
    {
        if (!HistoryIds.IsSafe(conversationId)) return;
        Directory.CreateDirectory(directory);
        var entry = new Entry(m.Id, m.Role, m.Text, m.At, m.BackendId,
            m.Attachments.Select(a => new AttachmentInfo(a.Name, a.Kind, a.MimeType)).ToList());
        File.AppendAllText(PathFor(conversationId), JsonSerializer.Serialize(entry, Json) + "\n");
    }

    public IReadOnlyList<Entry> Load(string conversationId)
    {
        if (!HistoryIds.IsSafe(conversationId)) return [];
        var path = PathFor(conversationId);
        if (!File.Exists(path)) return [];
        // One damaged line (a crash mid-append, a hand edit) skips only that line, never the whole chat.
        return File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).Select(TryEntry).OfType<Entry>().ToList();
    }

    private static Entry? TryEntry(string line)
    {
        try { return JsonSerializer.Deserialize<Entry>(line, Json) is { Id: not null, Text: not null } e ? e : null; }
        catch (JsonException) { return null; }
    }

    public void SaveMeta(string conversationId, Meta meta)
    {
        if (!HistoryIds.IsSafe(conversationId)) return;
        Directory.CreateDirectory(directory);
        var path = MetaPathFor(conversationId);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(meta, Json));
        File.Move(temp, path, overwrite: true);
    }

    public Meta? LoadMeta(string conversationId)
    {
        if (!HistoryIds.IsSafe(conversationId)) return null;
        var path = MetaPathFor(conversationId);
        if (!File.Exists(path)) return null;
        var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(path), Json);
        // The session ID goes back on a command line: drop anything that isn't a plain ID.
        return meta is null ? null : CliSession.IsValidId(meta.SessionId) ? meta : meta with { SessionId = null };
    }

    public int Prune(int retentionDays)
    {
        if (!Directory.Exists(directory)) return 0;
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        var deleted = 0;
        // A chat's transcript and record go together, judged by the newer of the two.
        foreach (var chat in Directory.EnumerateFiles(directory).Where(f => IdOf(Path.GetFileName(f)) is not null).GroupBy(f => IdOf(Path.GetFileName(f))))
        {
            if (chat.Max(File.GetLastWriteTimeUtc) >= cutoff) continue;
            foreach (var file in chat)
            {
                try { File.Delete(file); deleted++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // in use: the next start tries again
            }
        }
        return deleted;
    }
}
