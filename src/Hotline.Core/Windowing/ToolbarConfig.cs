using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Windowing;

/// <summary>
/// ~/.hotline/toolbar.json: the button rows around the message box and what they show, in order: "items" below it
/// (the original bottom bar) and "top" above it (empty by default). Leave an item out to hide it; "spacer" takes the free
/// space (repeatable). An item sits in one row only (in both: the bottom one). Unknown or repeated items are ignored; a
/// broken file falls back to the defaults without being overwritten.
/// </summary>
public sealed class ToolbarConfig
{
    public static readonly IReadOnlyList<string> Available =
        ["pin", "captureWindow", "captureScreen", "captureRegion", "spacer", "effort", "model", "provider", "prompt", "recent", "continueIn", "newChat", "settings"];

    /// <summary>The owner's layout (2026-10-16): no spacer (the pickers follow the capture buttons), plus Continue in.</summary>
    public static readonly IReadOnlyList<string> Defaults =
        ["pin", "captureWindow", "captureScreen", "captureRegion", "effort", "model", "provider", "prompt", "recent", "continueIn", "newChat", "settings"];

    public IReadOnlyList<string> Items { get; init; } = Defaults;
    /// <summary>The row above the message box.</summary>
    public IReadOnlyList<string> Top { get; init; } = [];
    public string? Error { get; init; }

    private static string List(IEnumerable<string> items) => string.Join(", ", items.Select(d => $"\"{d}\""));

    private static string FileText(IEnumerable<string> items, IEnumerable<string> top) => $$"""
        {
          // The button rows, left to right: "top" above the message box, "items" below it. Remove an item to hide it;
          // "spacer" takes the free space. An item sits in one row only.
          // Available: {{List(Available)}}
          "top": [{{List(top)}}],
          "items": [{{List(items)}}]
        }
        """;

    /// <summary>Writes the rows (from Settings › Appearance); the previous file is kept as toolbar.json.bak.</summary>
    public static void Save(string path, IReadOnlyList<string> items, IReadOnlyList<string>? top = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, FileText(items, top ?? []));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Moves the item at <paramref name="index"/> of one row to the end of the other (only that one: spacers repeat).</summary>
    public static (IReadOnlyList<string> Top, IReadOnlyList<string> Items) ToRow(IReadOnlyList<string> top, IReadOnlyList<string> items, int index, bool fromTop)
    {
        var from = (fromTop ? top : items).ToList();
        var to = (fromTop ? items : top).ToList();
        if (index < 0 || index >= from.Count) return (top, items);
        to.Add(from[index]);
        from.RemoveAt(index);
        return fromTop ? (from, to) : (to, from);
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
                File.WriteAllText(path, FileText(Defaults, []));
                return new ToolbarConfig();
            }
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            List<string> Row(string name, HashSet<string> seen) => (root?[name] as JsonArray ?? [])
                .Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? Available.FirstOrDefault(a => a.Equals(s, StringComparison.OrdinalIgnoreCase)) : null)
                .OfType<string>()
                .Where(i => i == "spacer" || seen.Add(i))
                .ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = Row("items", seen); // the bottom row wins an item listed in both
            var top = Row("top", seen);
            return items.Count == 0 && top.Count == 0 ? new ToolbarConfig() : new ToolbarConfig { Items = items, Top = top };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new ToolbarConfig { Error = L.F(Strings.Bar_Unreadable, ex.Message) };
        }
    }
}
