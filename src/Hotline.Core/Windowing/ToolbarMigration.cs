using System.Text.Json.Nodes;

namespace Hotline.Core.Windowing;

/// <summary>
/// toolbar.json v1 (two plain arrays, "spacer" taking the free space) to the v2 model. The first spacer of a row splits it
/// (items before it go left, after it right); further spacers are dropped, so the bar looks the same. The v1 reading rules
/// are kept (ids case-insensitive, unknown and non-text entries dropped, an item once only, the bottom row winning one
/// listed in both), then the result is written as a v2 document and read by <see cref="ToolbarModel.Resolve"/>, so it gets
/// exactly the validation of any v2 file (caps). The pickers were one block in v1 (drawn together where the first was), so they
/// are gathered there first; in v2 they are separate items and stay wherever the file puts them.
/// </summary>
public static class ToolbarMigration
{
    private static readonly string[] V1Ids =
        ["pin", "captureWindow", "captureScreen", "captureRegion", "spacer", "effort", "model", "provider", "prompt", "recent", "continueIn", "newChat", "settings"];

    /// <summary>The model for v1 text; <see cref="ToolbarModel.Defaults"/> when it is unreadable or lists nothing valid. Never throws.</summary>
    public static ToolbarModel FromV1(string json)
    {
        try
        {
            if (ToolbarModel.Parse(json, []) is not JsonObject root) return ToolbarModel.Defaults;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var bottom = Row(root["items"], seen); // the bottom row wins an item listed in both
            var top = Row(root["top"], seen);
            if (bottom.Items.Count == 0 && top.Items.Count == 0) return ToolbarModel.Defaults;
            (bottom, top) = GatherPickers(bottom, top);

            JsonObject Group((List<string> Items, int Split) row) => new()
            {
                ["left"] = new JsonArray([.. row.Items.Take(row.Split).Select(i => (JsonNode)i)]),
                ["right"] = new JsonArray([.. row.Items.Skip(row.Split).Select(i => (JsonNode)i)]),
            };
            return ToolbarModel.Resolve(new JsonObject
            {
                ["version"] = ToolbarModel.CurrentVersion,
                ["top"] = Group(top),
                ["items"] = Group(bottom),
            }.ToJsonString());
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or InvalidCastException)
        {
            return ToolbarModel.Defaults;
        }
    }

    /// <summary>v1 drew the pickers as one block where the first was (the bottom row first), in the order listed: the same here.</summary>
    private static ((List<string> Items, int Split), (List<string> Items, int Split)) GatherPickers(
        (List<string> Items, int Split) bottom, (List<string> Items, int Split) top)
    {
        var all = bottom.Items.Concat(top.Items).Where(ToolbarPlan.IsPicker).ToList();
        if (all.Count == 0) return (bottom, top);
        var inBottom = bottom.Items.Any(ToolbarPlan.IsPicker);
        var home = inBottom ? bottom : top;
        var at = home.Items.FindIndex(ToolbarPlan.IsPicker);
        (List<string>, int) Without((List<string> Items, int Split) row) =>
            ([.. row.Items.Where(i => !ToolbarPlan.IsPicker(i))], row.Split - row.Items.Take(row.Split).Count(ToolbarPlan.IsPicker));
        var (b, t) = (Without(bottom), Without(top));
        ref var target = ref (inBottom ? ref b : ref t);
        var wasLeft = at < home.Split;
        target.Item1.InsertRange(at, all);
        if (wasLeft) target.Item2 += all.Count; // the block joins the group the first picker was in
        return (b, t);
    }

    /// <summary>One v1 row without its spacers, and the index where the first spacer stood (everything when there was none).</summary>
    private static (List<string> Items, int Split) Row(JsonNode? node, HashSet<string> seen)
    {
        var items = new List<string>();
        int? split = null;
        foreach (var entry in node as JsonArray ?? [])
        {
            var id = entry is JsonValue v && v.TryGetValue<string>(out var s) ? V1Ids.FirstOrDefault(a => a.Equals(s, StringComparison.OrdinalIgnoreCase)) : null;
            if (id is null) continue;
            if (id == "spacer") split ??= items.Count;
            else if (seen.Add(id)) items.Add(id);
        }
        return (items, split ?? items.Count);
    }
}
