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

    public string PathFor(string conversationId) => Path.Combine(directory, $"{conversationId}.jsonl");

    private string MetaPathFor(string conversationId) => Path.Combine(directory, conversationId + MetaSuffix);

    private static string? IdOf(string fileName) =>
        fileName.EndsWith(MetaSuffix, StringComparison.OrdinalIgnoreCase) ? fileName[..^MetaSuffix.Length]
        : fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ? fileName[..^".jsonl".Length]
        : null;

    public void Append(string conversationId, ChatMessage m)
    {
        Directory.CreateDirectory(directory);
        var entry = new Entry(m.Id, m.Role, m.Text, m.At, m.BackendId,
            m.Attachments.Select(a => new AttachmentInfo(a.Name, a.Kind, a.MimeType)).ToList());
        File.AppendAllText(PathFor(conversationId), JsonSerializer.Serialize(entry, Json) + "\n");
    }

    public IReadOnlyList<Entry> Load(string conversationId)
    {
        var path = PathFor(conversationId);
        if (!File.Exists(path)) return [];
        return File.ReadLines(path).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<Entry>(l, Json)!).ToList();
    }

    public void SaveMeta(string conversationId, Meta meta)
    {
        Directory.CreateDirectory(directory);
        var path = MetaPathFor(conversationId);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(meta, Json));
        File.Move(temp, path, overwrite: true);
    }

    public Meta? LoadMeta(string conversationId)
    {
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
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (IdOf(Path.GetFileName(file)) is null || File.GetLastWriteTimeUtc(file) >= cutoff) continue;
            File.Delete(file);
            deleted++;
        }
        return deleted;
    }
}
