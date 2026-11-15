using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hotline.Core.Files;

public static class JsonMerge
{
    /// <summary>Applies serialized model differences in place. Arrays are replaced whole; unchanged keys retain their order.</summary>
    public static JsonObject Apply(JsonObject tree, JsonObject before, JsonObject after)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        Merge(tree, before, after);
        return tree;
    }

    public static JsonObject Apply<T>(JsonObject tree, T before, T after, JsonSerializerOptions? options = null)
    {
        var oldModel = JsonSerializer.SerializeToNode(before, options) as JsonObject;
        var newModel = JsonSerializer.SerializeToNode(after, options) as JsonObject;
        if (oldModel is null || newModel is null) throw new ArgumentException("Models must serialize to JSON objects.");
        return Apply(tree, oldModel, newModel);
    }

    private static void Merge(JsonObject tree, JsonObject? before, JsonObject after)
    {
        var keys = after.Select(p => p.Key).Concat(before?.Select(p => p.Key) ?? []).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var key in keys)
        {
            var previous = before?[key];
            var next = after[key];
            if (JsonNode.DeepEquals(previous, next)) continue;
            if (next is JsonObject nextObject)
            {
                if (tree[key] is JsonObject existing) Merge(existing, previous as JsonObject, nextObject);
                else
                {
                    var added = new JsonObject();
                    Merge(added, previous as JsonObject, nextObject);
                    tree[key] = added;
                }
            }
            else if (next is not null || tree.ContainsKey(key)) tree[key] = next?.DeepClone();
        }
    }
}
