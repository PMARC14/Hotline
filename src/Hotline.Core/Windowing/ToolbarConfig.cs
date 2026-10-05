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
        ["pin", "captureWindow", "captureScreen", "captureRegion", "spacer", "effort", "model", "provider", "prompt", "recent", "newChat", "settings"];

    public IReadOnlyList<string> Items { get; init; } = Defaults;
    public string? Error { get; init; }

    private static string FileText(IEnumerable<string> items) => $$"""
        {
          // The bottom bar, left to right. Remove an item to hide it; "spacer" takes the free space.
          // Available: {{string.Join(", ", Available.Select(a => $"\"{a}\""))}}
          "items": [{{string.Join(", ", items.Select(d => $"\"{d}\""))}}]
        }
        """;

    /// <summary>Writes the bar (from Settings › Appearance); the previous file is kept as toolbar.json.bak.</summary>
    public static void Save(string path, IReadOnlyList<string> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, FileText(items));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Adds an item next to the item that precedes it in the default order (or first).</summary>
    public static IReadOnlyList<string> Show(IReadOnlyList<string> items, string item)
    {
        if (items.Contains(item) && item != "spacer") return items;
        var list = items.ToList();
        var rank = Available.ToList().IndexOf(item);
        var at = 0;
        for (var i = list.Count - 1; i >= 0; i--)
            if (Available.ToList().IndexOf(list[i]) is var r && r >= 0 && r < rank) { at = i + 1; break; }
        list.Insert(at, item);
        return list;
    }

    public static IReadOnlyList<string> Hide(IReadOnlyList<string> items, string item) => items.Where(i => i != item).ToList();

    /// <summary>Moves the item at <paramref name="index"/> by <paramref name="delta"/> (unchanged at either end).</summary>
    public static IReadOnlyList<string> Move(IReadOnlyList<string> items, int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= items.Count || target < 0 || target >= items.Count) return items;
        var list = items.ToList();
        (list[index], list[target]) = (list[target], list[index]);
        return list;
    }

    public static ToolbarConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, FileText(Defaults));
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
