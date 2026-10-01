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
