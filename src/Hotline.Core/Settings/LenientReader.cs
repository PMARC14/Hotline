using System.Collections;
using System.Reflection;
using System.Text.Json;
using Hotline.Core.Diagnostics;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Settings;

/// <summary>What went wrong while reading one file, as short sentences (at most <see cref="Max"/> are kept).</summary>
public sealed class ReadProblems
{
    public const int Max = 50;
    private readonly List<string> _all = [];
    public IReadOnlyList<string> All => _all;
    public void Add(string problem) { if (_all.Count < Max) _all.Add(problem); }
}

/// <summary>
/// Reads settings.json and connection files one field at a time: a field of the wrong kind, an unknown enum word or a
/// number that doesn't fit keeps its default and costs one warning; nothing else in the file is affected. Text from the
/// file only ever appears in a warning as cleaned, shortened plain text.
/// </summary>
internal static class LenientReader
{
    private const int MaxValueText = 40;

    // The same reading rules as the typed serializer (enum words in any case, no numbers for enums).
    private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The file's limits that Normalize would otherwise clamp silently: a value outside them is reported.</summary>
    private sealed record Range(double Min, double Max, bool ZeroToo = false);

    private static readonly Dictionary<string, Range> Ranges = new()
    {
        ["ChatSettings.MaxImagePixels"] = new(256, 8192),
        ["ChatSettings.HistoryRetentionDays"] = new(1, 3650),
        ["ChatSettings.ThinkingDelayMs"] = new(0, 10_000),
        ["WindowSettings.FontSize"] = new(10, 32),
        ["WindowSettings.Width"] = new(320, 8000),
        ["WindowSettings.ChatHeight"] = new(60, 8000),
        ["WindowSettings.ChatWidth"] = new(320, 8000),
        ["WindowSettings.ChatWidthPercent"] = new(20, 90, ZeroToo: true),
        ["WindowSettings.TintOpacity"] = new(0, 1),
        ["WindowSettings.LuminosityOpacity"] = new(0, 1),
        ["WindowSettings.WidthPercent"] = new(20, 90),
        ["WindowSettings.MinWidth"] = new(320, 4000),
        ["WindowSettings.MaxWidth"] = new(320, 4000),
        ["WindowSettings.Height"] = new(0, 4000),
        ["WindowSettings.MaxHeightPercent"] = new(30, 95),
        ["PanelAnchor.X"] = new(0, 1),
        ["PanelAnchor.Y"] = new(0, 1),
        ["BackendProfile.MaxTokens"] = new(1, 1_000_000),
        ["BackendProfile.Temperature"] = new(0, 2),
    };

    private static readonly HashSet<string> Skipped = ["ChatSettings.Backends"]; // the connections have their own files

    private static readonly NullabilityInfoContext Nullability = new();

    private static readonly Dictionary<Type, PropertyInfo[]> PropertyCache = [];

    /// <summary>Parses the text as a JSON object; otherwise says why not (and the file is then left alone).</summary>
    public static bool TryParse(string text, string label, out JsonDocument? document, out string? problem)
    {
        document = null;
        try
        {
            var doc = JsonDocument.Parse(text, DocumentOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                problem = L.F(Strings.Settings_ProblemNotObject, label);
                return false;
            }
            document = doc;
            problem = null;
            return true;
        }
        catch (JsonException ex) // includes nesting deeper than the parser allows
        {
            problem = L.F(Strings.Settings_ProblemUnreadable, label, Short(ex.Message, 100));
            return false;
        }
    }

    /// <summary>Fills <paramref name="target"/> from a JSON object. Unknown keys are ignored; sections are filled recursively.</summary>
    public static void Populate(JsonElement obj, object target, string label, string path, ReadProblems problems)
    {
        var type = target.GetType();
        var keys = new Dictionary<string, (JsonElement Value, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in obj.EnumerateObject())
            keys[member.Name] = (member.Value, keys.TryGetValue(member.Name, out var seen) ? seen.Count + 1 : 1); // the last one wins

        foreach (var prop in Properties(type))
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            if (Skipped.Contains($"{type.Name}.{prop.Name}") || !keys.TryGetValue(name, out var found)) continue;
            var where = $"{label} › {(path.Length == 0 ? name : $"{path}.{name}")}";
            var childPath = path.Length == 0 ? name : $"{path}.{name}";
            if (found.Count > 1) problems.Add(L.F(Strings.Settings_ProblemDuplicate, where));
            var value = found.Value;

            if (IsSection(prop.PropertyType))
            {
                if (value.ValueKind != JsonValueKind.Object) { problems.Add(WrongType(where, value)); continue; }
                var section = prop.GetValue(target) ?? Activator.CreateInstance(prop.PropertyType)!;
                Populate(value, section, label, childPath, problems);
                prop.SetValue(target, section);
                continue;
            }

            try
            {
                if (value.ValueKind == JsonValueKind.Null && !AllowsNull(prop)) throw new JsonException("null");
                var read = value.Deserialize(prop.PropertyType, SettingsStore.Options);
                if (read is List<string> list) list.RemoveAll(x => x is null);
                if (read is not null && !Valid(type, prop, read)) { problems.Add(WrongType(where, value)); continue; }
                if (read is not null && Ranges.TryGetValue($"{type.Name}.{prop.Name}", out var range) && Number(read) is { } number
                    && !(number >= range.Min && number <= range.Max || range.ZeroToo && number == 0))
                    problems.Add(L.F(Strings.Settings_ProblemRange, where, Describe(value), range.Min, range.Max)); // kept; Normalize pulls it in
                prop.SetValue(target, read);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException or OverflowException)
            {
                problems.Add(Describe(where, $"{type.Name}.{prop.Name}", prop.PropertyType, value));
            }
        }
    }

    /// <summary>
    /// One connection file (or old inline entry). Null when it can't be used: an unknown or non-text "type" would otherwise
    /// fall back to the first type, which starts a CLI.
    /// </summary>
    public static BackendProfile? ReadProfile(JsonElement obj, string label, ReadProblems problems)
    {
        foreach (var member in obj.EnumerateObject().Where(m => m.Name.Equals("type", StringComparison.OrdinalIgnoreCase)))
        {
            var known = member.Value.ValueKind == JsonValueKind.String
                        && Enum.GetNames<BackendType>().Any(n => n.Equals(member.Value.GetString(), StringComparison.OrdinalIgnoreCase));
            if (known) continue;
            problems.Add(L.F(Strings.Settings_ProblemConnectionType, label, Describe(member.Value)));
            return null;
        }
        var profile = new BackendProfile();
        Populate(obj, profile, label, "", problems);
        return profile;
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static PropertyInfo[] Properties(Type type)
    {
        lock (PropertyCache)
        {
            if (!PropertyCache.TryGetValue(type, out var props))
                PropertyCache[type] = props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0).ToArray();
            return props;
        }
    }

    private static bool IsSection(Type t) => t.IsClass && t != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(t);

    private static bool AllowsNull(PropertyInfo prop)
    {
        if (Nullable.GetUnderlyingType(prop.PropertyType) is not null) return true;
        if (prop.PropertyType.IsValueType) return false;
        lock (Nullability) return Nullability.Create(prop).WriteState != NullabilityState.NotNull;
    }

    // Values the type accepts but the program can't use (an unparsable stop shortcut would silently become Esc).
    private static bool Valid(Type owner, PropertyInfo prop, object value) =>
        !(owner == typeof(ChatSettings) && prop.Name == nameof(ChatSettings.StopShortcut) && value is string s && !Activation.Hotkey.TryParseShortcut(s, out _));

    private static double? Number(object value) => value switch
    {
        int i => i,
        double d => d,
        _ => null,
    };

    private static string Describe(string where, string key, Type type, JsonElement value)
    {
        var enumType = Nullable.GetUnderlyingType(type) ?? type;
        if (enumType.IsEnum && value.ValueKind == JsonValueKind.String)
            return L.F(Strings.Settings_ProblemUnknown, where, Describe(value),
                string.Join(", ", Enum.GetNames(enumType).Select(n => JsonNamingPolicy.CamelCase.ConvertName(n))));
        if (value.ValueKind == JsonValueKind.Number && (enumType == typeof(int) || enumType == typeof(double)))
        {
            var (min, max) = Ranges.TryGetValue(key, out var range) ? (range.Min, range.Max)
                : enumType == typeof(int) ? ((double)int.MinValue, (double)int.MaxValue) : (-double.MaxValue, double.MaxValue);
            return L.F(Strings.Settings_ProblemRange, where, Describe(value), min, max);
        }
        return WrongType(where, value);
    }

    private static string WrongType(string where, JsonElement value) => L.F(Strings.Settings_ProblemWrongType, where, Describe(value));

    /// <summary>The value as plain text for a warning: cleaned (never a forged line, no secrets), then shortened.</summary>
    public static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{…}",
        JsonValueKind.Array => "[…]",
        JsonValueKind.String => $"\"{Short(value.GetString(), MaxValueText)}\"",
        JsonValueKind.Null => "null",
        _ => Short(value.GetRawText(), MaxValueText),
    };

    public static string Quote(string? text) => $"\"{Short(text, MaxValueText)}\"";

    private static string Short(string? text, int max)
    {
        var clean = LogText.Clean(text).Replace("\n    ", " ").Replace('\n', ' ');
        return clean.Length > max ? clean[..(max - 1)] + "…" : clean;
    }
}
