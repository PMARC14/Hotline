using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hotline.Core.Files;

/// <summary>Outcome of <see cref="JsonFile.Save"/>: what happened and the version the file on disk carried (when it had one).</summary>
public readonly record struct JsonSaveResult(SafeFileResult Result, int? DiskVersion)
{
    public bool Ok => Result is SafeFileResult.Written or SafeFileResult.Unchanged;
}

/// <summary>Shared JSON file mechanics for config and state files: BOM, lenient parse, one version reader, merge, single-read save.</summary>
public static class JsonFile
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    public static ReadOnlySpan<byte> StripBom(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Bom) ? bytes[3..] : bytes;

    /// <summary>Parses a JSON object (BOM, comments and trailing commas allowed). False for anything else, including empty or invalid text.</summary>
    public static bool TryParseObject(ReadOnlySpan<byte> bytes, out JsonObject obj)
    {
        obj = null!;
        try
        {
            var text = Encoding.UTF8.GetString(StripBom(bytes));
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (JsonNode.Parse(text, documentOptions: ReadOptions) is not JsonObject parsed) return false;
            obj = parsed;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or DecoderFallbackException) { return false; }
    }

    /// <summary>The <c>version</c> number of a JSON object (key matched in any case, exact spelling first). Fractions round up.
    /// Null when the bytes are not an object or carry no numeric version.</summary>
    public static int? ReadVersion(ReadOnlySpan<byte> bytes)
    {
        try
        {
            // Lenient on purpose: a file with duplicate keys is still checked, so it is never taken for an older one.
            var reader = new Utf8JsonReader(StripBom(bytes), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            JsonElement? found = null;
            foreach (var member in document.RootElement.EnumerateObject())
                if (member.Name == "version") found = member.Value;
            if (found is null)
                foreach (var member in document.RootElement.EnumerateObject())
                    if (member.Name.Equals("version", StringComparison.OrdinalIgnoreCase)) { found = member.Value; break; }
            return found is { ValueKind: JsonValueKind.Number } n && n.TryGetDouble(out var d) && double.IsFinite(d)
                ? (int)Math.Clamp(Math.Ceiling(d), int.MinValue, int.MaxValue) : null;
        }
        catch (JsonException) { return null; }
    }

    public static int? ReadVersion(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (FindKey(root, "version") is not { } key || root[key] is not JsonValue value) return null;
        if (value.TryGetValue<int>(out var whole)) return whole;
        if (value.TryGetValue<double>(out var d) && double.IsFinite(d)) return (int)Math.Clamp(Math.Ceiling(d), int.MinValue, int.MaxValue);
        return null;
    }

    /// <summary>Exact key if present, otherwise the first key equal in any case.</summary>
    public static string? FindKey(JsonObject obj, string name)
    {
        if (obj.ContainsKey(name)) return name;
        foreach (var (key, _) in obj)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return key;
        return null;
    }

    /// <summary>Applies the difference between two models onto the on-disk tree, keeping the tree's own key spelling
    /// (a hand-edited <c>"Shown"</c> stays <c>"Shown"</c>) and everything the models do not know.</summary>
    public static JsonObject Merge(JsonObject tree, JsonObject before, JsonObject after)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var oldModel = (JsonObject)before.DeepClone();
        var newModel = (JsonObject)after.DeepClone();
        AlignCase(tree, oldModel);
        AlignCase(tree, newModel);
        return JsonMerge.Apply(tree, oldModel, newModel);
    }

    private static void AlignCase(JsonObject tree, JsonObject model)
    {
        foreach (var key in model.Select(p => p.Key).ToArray())
        {
            var diskKey = FindKey(tree, key);
            var node = model[key];
            if (diskKey is not null && diskKey != key)
            {
                model.Remove(key);
                model[diskKey] = node;
            }
            if (node is JsonObject child && tree[diskKey ?? key] is JsonObject diskChild) AlignCase(diskChild, child);
        }
    }

    /// <summary>Reads the file once, refuses to replace a newer-version file, skips identical bytes, and publishes durably.
    /// State-style files are never backed up; use <see cref="SafeFile"/> for files that need a backup set.</summary>
    public static JsonSaveResult Save(IFileSystem files, string path, byte[] bytes, int version)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(bytes);
        path = Path.GetFullPath(path);
        int? diskVersion = null;
        try
        {
            if (files.FileExists(path))
            {
                var original = files.ReadAllBytes(path);
                diskVersion = ReadVersion(original);
                if (diskVersion > version) return new(SafeFileResult.NewerVersion, diskVersion);
                if (original.AsSpan().SequenceEqual(bytes)) return new(SafeFileResult.Unchanged, diskVersion);
            }
            files.CreateDirectory(Path.GetDirectoryName(path)!);
            files.WriteDurable(path, bytes);
            return new(SafeFileResult.Written, diskVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(SafeFileResult.WriteFailed, diskVersion); }
    }

    public static byte[] ToBytes(JsonNode node, JsonSerializerOptions options) => Utf8NoBom.GetBytes(node.ToJsonString(options));
}
