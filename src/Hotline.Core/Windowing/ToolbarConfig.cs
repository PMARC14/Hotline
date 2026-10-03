using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hotline.Core.Windowing;

/// <summary>
/// ~/.hotline/toolbar.json: which controls the bottom bar shows and in what order ("items"); leave one out to hide it,
/// "spacer" takes the free space (repeatable). Unknown or repeated items are ignored; a broken file falls back to the
/// defaults without being overwritten.
/// </summary>
public sealed class ToolbarConfig
{
    public static readonly IReadOnlyList<string> Available =
        ["pin", "captureWindow", "captureScreen", "captureRegion", "spacer", "effort", "model", "provider", "prompt", "recent", "newChat", "settings"];

    public static readonly IReadOnlyList<string> Defaults =
        ["pin", "captureWindow", "captureScreen", "spacer", "effort", "model", "provider", "prompt", "recent", "newChat", "settings"];

    public IReadOnlyList<string> Items { get; init; } = Defaults;
    public string? Error { get; init; }

    public static ToolbarConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, $$"""
                    {
                      // The bottom bar, left to right. Remove an item to hide it; "spacer" takes the free space.
                      // Available: {{string.Join(", ", Available.Select(a => $"\"{a}\""))}}
                      "items": [{{string.Join(", ", Defaults.Select(d => $"\"{d}\""))}}]
                    }
                    """);
                return new ToolbarConfig();
            }
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = (root?["items"] as JsonArray ?? [])
                .Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? Available.FirstOrDefault(a => a.Equals(s, StringComparison.OrdinalIgnoreCase)) : null)
                .OfType<string>()
                .Where(i => i == "spacer" || seen.Add(i))
                .ToList();
            return new ToolbarConfig { Items = items.Count > 0 ? items : Defaults };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new ToolbarConfig { Error = $"toolbar.json couldn't be read: {ex.Message}" };
        }
    }
}
