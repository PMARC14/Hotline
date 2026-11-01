using System.Text.Json;
using Hotline.Core.Chat;

namespace Hotline.Core.Backends;

public static partial class CliTranscripts
{
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

}
