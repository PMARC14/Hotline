using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hotline.Core.Chat;

/// <summary>One JSONL file per conversation. Attachments are recorded by name/kind only, never their bytes.</summary>
public sealed class HistoryStore(string directory, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public sealed record AttachmentInfo(string Name, AttachmentKind Kind, string MimeType);
    public sealed record Entry(string Id, ChatRole Role, string Text, DateTimeOffset At, string? BackendId, IReadOnlyList<AttachmentInfo> Attachments);

    /// <summary>One line in the Recent menu.</summary>
    public sealed record Summary(string Id, string Title, DateTimeOffset LastAt, int MessageCount);

    /// <summary>The most recently used conversations, newest first. Unreadable or empty files are skipped.</summary>
    public IReadOnlyList<Summary> Recent(int count)
    {
        if (!Directory.Exists(directory)) return [];
        var result = new List<Summary>();
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc))
        {
            if (result.Count >= count) break;
            try
            {
                var id = Path.GetFileNameWithoutExtension(file.Name);
                var entries = Load(id);
                if (entries.Count == 0) continue;
                var first = entries.FirstOrDefault(e => e.Role == ChatRole.User && e.Text.Trim().Length > 0)?.Text ?? entries[0].Text;
                var title = string.Join(' ', first.Split((char[])['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
                if (title.Length > 60) title = title[..60].TrimEnd() + "…";
                result.Add(new Summary(id, title.Length > 0 ? title : "(attachments)", entries[^1].At, entries.Count));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        }
        return result;
    }

    public string PathFor(string conversationId) => Path.Combine(directory, $"{conversationId}.jsonl");

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

    public int Prune(int retentionDays)
    {
        if (!Directory.Exists(directory)) return 0;
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl"))
        {
            if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
            File.Delete(file);
            deleted++;
        }
        return deleted;
    }
}
