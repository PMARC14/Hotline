using System.Text.Json;
using Hotline.Core.Chat;

namespace Hotline.Core.Backends;

public static partial class CliTranscripts
{
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

}
