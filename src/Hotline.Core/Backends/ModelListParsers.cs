using System.Text.Json;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Backends;

public static class ModelListParsers
{
    public static IReadOnlyList<ModelInfo> ClaudeCodeAliases { get; } =
        [new("fable", "Fable"), new("opus", "Opus"), new("sonnet", "Sonnet"), new("haiku", "Haiku")];

    /// <summary>`agy models` prints "id\tLabel" per line (plus a "Fetching…" banner).</summary>
    public static IReadOnlyList<ModelInfo> Agy(string output) =>
        output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Contains('\t'))
            .Select(l => l.Split('\t', 2)).Select(p => new ModelInfo(p[0].Trim(), p[1].Trim())).ToList();

    /// <summary>A string property, or null when the entry isn't an object or the field is missing, null or not a string.</summary>
    private static string? Text(JsonElement entry, string name) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static IReadOnlyList<ModelInfo> OpenAi(string json) => Parse(json, root =>
        root.GetProperty("data").EnumerateArray().Select(m => Text(m, "id")).OfType<string>().Where(id => id.Length > 0)
            .Order(StringComparer.Ordinal).Select(id => new ModelInfo(id, id)).ToList());

    public static IReadOnlyList<ModelInfo> Gemini(string json) => Parse(json, root =>
        root.TryGetProperty("models", out var models)
            ? models.EnumerateArray()
                .Where(m => m.ValueKind == JsonValueKind.Object && m.TryGetProperty("supportedGenerationMethods", out var g)
                    && g.ValueKind == JsonValueKind.Array && g.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "generateContent"))
                .Select(m => Text(m, "name")?.Replace("models/", "", StringComparison.Ordinal) is { Length: > 0 } id
                    ? new ModelInfo(id, Text(m, "displayName") ?? id) : null)
                .OfType<ModelInfo>().ToList()
            : []);

    public static IReadOnlyList<ModelInfo> Anthropic(string json) => Parse(json, root =>
        root.GetProperty("data").EnumerateArray()
            .Select(m => Text(m, "id") is { Length: > 0 } id ? new ModelInfo(id, Text(m, "display_name") ?? id) : null)
            .OfType<ModelInfo>().ToList());

    private static IReadOnlyList<ModelInfo> Parse(string json, Func<JsonElement, IReadOnlyList<ModelInfo>> read)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return read(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ModelListException(Strings.Models_BadFormat);
        }
    }
}
