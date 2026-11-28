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

    // The one rule for a file's version, used by every reader and writer: the HIGHEST numeric value among all keys named
    // "version" in any case (a hand edit can leave several). No writer then replaces a file any reader treats as newer.
    // VersionValue is the raw number (pre-freeze checks compare it); ReadVersion rounds it up to an int.

    /// <summary>The file's version (see the rule above), fractions rounded up; null when the bytes are not an object or carry
    /// no numeric version. Lenient: comments, trailing commas, duplicate keys and trailing text are still checked.</summary>
    public static int? ReadVersion(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var reader = new Utf8JsonReader(StripBom(bytes), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            using var document = JsonDocument.ParseValue(ref reader);
            return Rounded(VersionValue(document.RootElement));
        }
        catch (JsonException) { return null; }
    }

    public static int? ReadVersion(JsonObject root) => Rounded(VersionValue(root));

    public static double? VersionValue(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        double? highest = null;
        foreach (var member in root.EnumerateObject())
            if (member.Name.Equals("version", StringComparison.OrdinalIgnoreCase) && member.Value.ValueKind == JsonValueKind.Number
                && member.Value.TryGetDouble(out var d) && !double.IsNaN(d) && (highest is null || d > highest))
                highest = d;
        return highest;
    }

    public static double? VersionValue(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        double? highest = null;
        foreach (var (key, node) in root)
            if (key.Equals("version", StringComparison.OrdinalIgnoreCase) && node is JsonValue value
                && value.GetValueKind() == JsonValueKind.Number && Number(value) is { } d && (highest is null || d > highest))
                highest = d;
        return highest;
    }

    /// <summary>A number node as a double, whether parsed (element-backed) or built in code (int, long, decimal...). A number
    /// too large for a double (e.g. 1e400) is +infinity: newer than anything, never written over.</summary>
    private static double? Number(JsonValue value) =>
        value.TryGetValue<double>(out var d) ? (double.IsNaN(d) ? null : d)
        : value.TryGetValue<long>(out var l) ? l
        : value.TryGetValue<int>(out var i) ? i
        : value.TryGetValue<decimal>(out var m) ? (double)m
        : null;

    private static int? Rounded(double? version) =>
        version is { } d ? (int)Math.Clamp(Math.Ceiling(d), int.MinValue, int.MaxValue) : null;

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
