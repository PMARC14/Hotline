using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hotline.Core.Windowing;

/// <summary>Where an item sits. The declaration order decides which place keeps an item listed in two.</summary>
public enum Quadrant { BottomLeft, BottomCenter, BottomRight, TopLeft, TopCenter, TopRight }

/// <summary>The size presets: half, once and twice one base size (see <see cref="ToolbarPlan.Metrics(ButtonStyle, bool)"/>);
/// custom buttons stop at 80 DIP.</summary>
public enum ButtonSize { Compact, Normal, Wide }

/// <summary>The look of the whole bar or of one item; a null field inherits (item, then bar, then the defaults).
/// Width and height (DIP) apply to icon buttons only; pickers ignore all of it. <see cref="OutlineColor"/> is a validated
/// "#RRGGBB" (upper case) for the outline, null for the accent colour.</summary>
public sealed record ButtonStyle(ButtonSize? Size = null, bool? Outline = null, bool? Filled = null, int? Width = null, int? Height = null,
    string? OutlineColor = null);

/// <summary>A user-defined button: runs the quick action of that name (a file in actions\) or inserts <see cref="Text"/>
/// in the message box (and sends it when <see cref="Send"/>). Exactly one of the two is set. <see cref="IconFile"/> is
/// its picture in icons\ (always "&lt;id&gt;.png", see <see cref="CustomIcons"/>), shown instead of the <see cref="Icon"/>
/// glyph when it can be read; the glyph stays as the fallback.</summary>
public sealed record CustomButton(string Id, string Label, string Icon, string? QuickAction, string? Text, bool Send, string? IconFile = null);

/// <summary>
/// ~/.hotline/toolbar.json v2, resolved: what each quadrant shows, the bar's look, per-item overrides and the custom buttons.
/// <see cref="Resolve"/> is the only reader: it never throws, falls back per field (noting each fix in
/// <see cref="Warnings"/>) and clamps everything, so the renderer and Settings only ever see a valid model.
/// Equality ignores <see cref="Warnings"/>.
/// </summary>
public sealed record ToolbarModel(
    IReadOnlyDictionary<Quadrant, IReadOnlyList<string>> Placed,
    ButtonStyle Style,
    IReadOnlyDictionary<string, ButtonStyle> Overrides,
    IReadOnlyList<CustomButton> Buttons,
    IReadOnlyList<string> Warnings)
{
    public const int CurrentVersion = 2;
    /// <summary>A picker is this many button widths wide, at least 1 and at most 8 (see <see cref="EffectiveSpan"/>).</summary>
    public const int MinSpan = 1, MaxSpan = 8;
    /// <summary>The glyph of a custom button whose icon is missing or invalid (Segoe Fluent Icons "FavoriteStar").</summary>
    public const string DefaultIcon = "E734";
    /// <summary>At most this many custom buttons, and items per quadrant (the rest of a longer list is ignored).</summary>
    public const int MaxButtons = 24, MaxItems = 40;
    /// <summary>A custom button id's longest length.</summary>
    public const int MaxId = 32;
    private const int MaxLabel = 40, MaxText = 2000, MaxActionName = 64, MinDip = 20, MaxDip = 80;

    public static readonly IReadOnlyList<string> BuiltIn =
        ["pin", "captureWindow", "captureScreen", "captureRegion", "effort", "model", "provider", "prompt", "recent", "continueIn", "newChat", "settings"];

    /// <summary>One block in the renderer (PickersPanel): they always end up adjacent.</summary>
    internal static readonly string[] Pickers = ["effort", "model", "provider"];

    private static readonly Quadrant[] Order = Enum.GetValues<Quadrant>();

    /// <summary>Each picker's width in button widths when the file doesn't say.</summary>
    private static int DefaultSpan(string id) => id.ToLowerInvariant() switch { "effort" => 2, "model" => 4, "provider" => 3, _ => 1 };

    /// <summary>The pickers' own widths, in button widths, for the ones that differ from the default (lower-case id to
    /// <see cref="MinSpan"/>-<see cref="MaxSpan"/>). Kept for a picker that is hidden, too.</summary>
    public IReadOnlyDictionary<string, int> PickerSpans { get; init; } = new Dictionary<string, int>();

    /// <summary>Today's layout (the owner's, 2026-10-16): pickers in the top row, the rest bottom left.</summary>
    public static ToolbarModel Defaults { get; } = new(
        Layout(q => q switch
        {
            Quadrant.BottomLeft => ["pin", "captureWindow", "captureScreen", "captureRegion", "prompt", "recent", "continueIn", "newChat", "settings"],
            Quadrant.TopLeft => ["effort", "model", "provider"],
            _ => [],
        }),
        new ButtonStyle(ButtonSize.Normal, false, false),
        new Dictionary<string, ButtonStyle>(StringComparer.OrdinalIgnoreCase),
        [],
        []);

    private static IReadOnlyDictionary<Quadrant, IReadOnlyList<string>> Layout(Func<Quadrant, IReadOnlyList<string>> items) =>
        Order.ToDictionary(q => q, items);

    public IReadOnlyList<string> Items(Quadrant quadrant) => Placed.TryGetValue(quadrant, out var items) ? items : [];

    /// <summary>The style of one item, per field: its override, else the bar's style, else the defaults.</summary>
    public ButtonStyle EffectiveStyle(string id)
    {
        var o = Overrides.GetValueOrDefault(id);
        return new ButtonStyle(
            o?.Size ?? Style.Size ?? Defaults.Style.Size,
            o?.Outline ?? Style.Outline ?? Defaults.Style.Outline,
            o?.Filled ?? Style.Filled ?? Defaults.Style.Filled,
            o?.Width ?? Style.Width,
            o?.Height ?? Style.Height,
            o?.OutlineColor ?? Style.OutlineColor);
    }

    /// <summary>How many button widths a picker is wide: its span, else its default (effort 2, model 4, provider 3), clamped to
    /// 1-8. 1 for anything that is not a picker.</summary>
    public int EffectiveSpan(string? id)
    {
        if (id is null || !Pickers.Contains(id, StringComparer.OrdinalIgnoreCase)) return MinSpan;
        var key = id.ToLowerInvariant();
        return Math.Clamp(PickerSpans.TryGetValue(key, out var span) ? span : DefaultSpan(key), MinSpan, MaxSpan);
    }

    public bool Equals(ToolbarModel? other) =>
        other is not null
        && Order.All(q => Items(q).SequenceEqual(other.Items(q)))
        && Style == other.Style
        && Overrides.Count == other.Overrides.Count
        && Overrides.All(o => other.Overrides.TryGetValue(o.Key, out var s) && s == o.Value)
        && Buttons.SequenceEqual(other.Buttons)
        && PickerSpans.Count == other.PickerSpans.Count
        && PickerSpans.All(p => other.PickerSpans.TryGetValue(p.Key, out var n) && n == p.Value);

    public override int GetHashCode() =>
        HashCode.Combine(Style, Overrides.Count, Buttons.Count, string.Join(",", Order.SelectMany(Items)));

    // ---- Reading ----

    private static readonly JsonDocumentOptions ParseOptions =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, AllowDuplicateProperties = false };
    private static readonly JsonDocumentOptions RepeatedKeys = ParseOptions with { AllowDuplicateProperties = true };

    /// <summary>Reads toolbar.json text. Never throws: unparsable text or a root that isn't an object gives
    /// <see cref="Defaults"/>; anything else is fixed per field.</summary>
    public static ToolbarModel Resolve(string json)
    {
        try
        {
            var warnings = new List<string>();
            var root = Parse(json, warnings);
            return root is JsonObject o ? Read(o, warnings) : Defaults with { Warnings = ["toolbar.json isn't a JSON object; the defaults are used"] };
        }
        catch (Exception ex) when (ex is JsonException or InvalidCastException or InvalidOperationException)
        {
            return Defaults with { Warnings = [$"toolbar.json can't be read ({Clean(ex.Message, 200, false)}); the defaults are used"] };
        }
    }

    /// <summary>The tolerant read shared by <see cref="Resolve"/> and the v1 migration: strips a BOM, accepts comments and
    /// trailing commas, and lets the last of a repeated key win (noted in <paramref name="warnings"/>). Throws
    /// <see cref="JsonException"/> for unparsable text (including empty text).</summary>
    internal static JsonNode? Parse(string? json, List<string> warnings)
    {
        json ??= "";
        if (json.StartsWith('﻿')) json = json[1..];
        try { return JsonNode.Parse(json, documentOptions: ParseOptions); }
        catch (JsonException strict)
        {
            // Valid apart from a repeated key: read it with the last value winning, a per-field fix like any other.
            using var doc = JsonDocument.Parse(json, RepeatedKeys);
            var root = LastWins(doc.RootElement.Clone());
            warnings.Add($"Repeated key in toolbar.json ({Clean(strict.Message, 200, false)}); the last value is used");
            return root;
        }
    }

    /// <summary>The tree of a document with repeated keys (JsonObject itself rejects them): a later key replaces an earlier one.</summary>
    private static JsonNode? LastWins(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var o = new JsonObject();
                foreach (var p in e.EnumerateObject()) o[p.Name] = LastWins(p.Value);
                return o;
            case JsonValueKind.Array:
                return new JsonArray([.. e.EnumerateArray().Select(LastWins)]);
            default:
                return JsonValue.Create(e);
        }
    }

    /// <summary>A v1 file: no (or an older) version and a row given as a plain array.</summary>
    internal static bool IsV1(JsonObject root) =>
        (root["version"] is null || (root["version"] is JsonValue v && v.TryGetValue<int>(out var n) && n < CurrentVersion))
        && (root["items"] is JsonArray || root["top"] is JsonArray);

    private static ToolbarModel Read(JsonObject root, List<string> warnings)
    {
        int? version = root["version"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;
        // A v1 file (no version, rows as plain arrays) is converted by the migration, not read here.
        if (IsV1(root))
            return Defaults with { Warnings = ["toolbar.json v1 needs migration"] };
        if (root["version"] is not null && version != CurrentVersion)
            warnings.Add($"toolbar.json version {Show(root["version"])} isn't {CurrentVersion}; read as far as it goes");

        var buttons = ReadButtons(root["buttons"], warnings);
        var known = BuiltIn.Concat(buttons.Select(b => b.Id)).ToDictionary(id => id, StringComparer.OrdinalIgnoreCase);
        var (placed, explicitlyEmpty) = ReadQuadrants(root, known, warnings);
        if (placed.Values.All(l => l.Count == 0) && !explicitlyEmpty)
        {
            warnings.Add("toolbar.json shows no items (an empty bar needs the left and right groups of both rows present and empty); the default layout is used");
            placed = Order.ToDictionary(q => q, q => Defaults.Items(q).ToList());
        }

        var style = ReadStyle(root["style"], "style", warnings);
        var overrides = ReadOverrides(root["overrides"], placed.Values.SelectMany(l => l), warnings, out var spans);
        return new ToolbarModel(
            Layout(q => placed[q]),
            style with { Size = style.Size ?? ButtonSize.Normal, Outline = style.Outline ?? false, Filled = style.Filled ?? false },
            overrides,
            buttons,
            warnings) { PickerSpans = spans };
    }

    private static (Dictionary<Quadrant, List<string>> Placed, bool ExplicitlyEmpty) ReadQuadrants(
        JsonObject root, Dictionary<string, string> known, List<string> warnings)
    {
        var placed = Order.ToDictionary(q => q, _ => new List<string>());
        var seen = new HashSet<string>();
        var explicitlyEmpty = true;
        foreach (var rowName in new[] { "items", "top" })
            if (root[rowName] is { } row && row is not JsonObject)
                warnings.Add($"\"{rowName}\" should be an object with \"left\" and \"right\"; ignored");
        foreach (var q in Order)
        {
            var rowName = q is Quadrant.BottomLeft or Quadrant.BottomCenter or Quadrant.BottomRight ? "items" : "top";
            var side = q is Quadrant.BottomLeft or Quadrant.TopLeft ? "left" : q is Quadrant.BottomCenter or Quadrant.TopCenter ? "center" : "right";
            if ((root[rowName] as JsonObject)?[side] is not JsonArray list)
            {
                if (side != "center") explicitlyEmpty = false; // the centre group is optional: a file without one is still an explicit empty bar
                if ((root[rowName] as JsonObject)?[side] is not null) warnings.Add($"\"{rowName}.{side}\" should be a list; ignored");
                continue;
            }
            if (list.Count > 0) explicitlyEmpty = false;
            if (list.Count > MaxItems) warnings.Add($"\"{rowName}.{side}\" lists more than {MaxItems} items; the rest are ignored");
            foreach (var entry in list.Take(MaxItems))
            {
                if (entry is not JsonValue value || !value.TryGetValue<string>(out var s)) { warnings.Add($"\"{rowName}.{side}\": {Show(entry)} isn't an item id; ignored"); continue; }
                if (s.Equals("spacer", StringComparison.OrdinalIgnoreCase)) continue; // v1 leftover: groups replace it
                if (!known.TryGetValue(s, out var id)) warnings.Add($"Unknown item \"{Clean(s, MaxLabel, false)}\" ignored");
                else if (seen.Add(id)) placed[q].Add(id); // listed again later: the first place wins
            }
        }

        // The pickers are one block: all of them go where the first one is, in the order they were listed.
        var first = Order.FirstOrDefault(q => placed[q].Any(Pickers.Contains), (Quadrant)(-1));
        if (Order.Contains(first))
        {
            var at = placed[first].FindIndex(Pickers.Contains);
            var block = Order.SelectMany(q => placed[q]).Where(Pickers.Contains).ToList();
            foreach (var q in Order) placed[q].RemoveAll(Pickers.Contains);
            placed[first].InsertRange(at, block);
        }
        return (placed, explicitlyEmpty);
    }

    private static List<CustomButton> ReadButtons(JsonNode? node, List<string> warnings)
    {
        var buttons = new List<CustomButton>();
        if (node is null) return buttons;
        if (node is not JsonObject all) { warnings.Add("\"buttons\" should be an object; ignored"); return buttons; }
        foreach (var (id, def) in all)
        {
            if (buttons.Count == MaxButtons) { warnings.Add($"More than {MaxButtons} custom buttons; the rest are ignored"); break; }
            var name = Clean(id, MaxLabel, false);
            if (!IsCustomId(id))
                { warnings.Add($"Custom button id \"{name}\" must be 1-{MaxId} characters a-z, 0-9 or -; ignored"); continue; }
            if (BuiltIn.Contains(id, StringComparer.OrdinalIgnoreCase)) { warnings.Add($"Custom button \"{name}\" has a built-in id; ignored"); continue; }
            if (def is not JsonObject b) { warnings.Add($"Custom button \"{id}\" should be an object; ignored"); continue; }

            var label = Clean(String(b, "label", id, warnings) ?? "", MaxLabel, false);
            var quickAction = String(b, "quickAction", id, warnings) is { } qa ? Clean(qa, int.MaxValue, false) : null;
            if (quickAction is not null && !IsActionName(quickAction))
                { warnings.Add($"Custom button \"{id}\": \"quickAction\" must be the name of a file in actions\\ (letters, digits, _ and -, at most {MaxActionName} characters); ignored"); continue; }
            var text = String(b, "text", id, warnings) is { } t && Clean(t, MaxText, true) is { Length: > 0 } x ? x : null;
            if ((quickAction is null) == (text is null)) { warnings.Add($"Custom button \"{id}\" needs exactly one of \"quickAction\" and \"text\"; ignored"); continue; }
            // Only the name is checked here (Resolve reads no files): the renderer falls back to the glyph if the picture is gone.
            var iconFile = String(b, "iconFile", id, warnings);
            if (iconFile is not null && !CustomIcons.IsFileName(iconFile, id))
            {
                warnings.Add($"\"buttons.{id}.iconFile\" {Show(b["iconFile"])} isn't \"{id}.png\" (a picture in icons\\); the icon is used");
                iconFile = null;
            }
            buttons.Add(new CustomButton(id, label.Length > 0 ? label : id, Icon(b["icon"], id, warnings), quickAction, text,
                text is not null && (Bool(b["send"], $"buttons.{id}.send", warnings) ?? false), iconFile));
        }
        return buttons;
    }

    /// <summary>The form of a custom button id: 1-32 characters a-z, 0-9 or - (whether it collides with a built-in is checked apart).
    /// "spacer" is reserved: the old v1 layout item of that name is ignored in the layout lists, so a button with it could never be shown.</summary>
    internal static bool IsCustomId(string? id) =>
        id is { Length: > 0 and <= MaxId } && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
        && !id.Equals("spacer", StringComparison.OrdinalIgnoreCase);

    /// <summary>The name of a file in actions\ as QuickActions reads and parses it (never a path), at most 64 characters.</summary>
    internal static bool IsActionName(string name) => name.Length <= MaxActionName && Chat.QuickActions.IsName(name);

    private static string? String(JsonObject o, string key, string id, List<string> warnings)
    {
        if (o[key] is null) return null;
        if (o[key] is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        warnings.Add($"\"buttons.{id}.{key}\" should be text; ignored");
        return null;
    }

    /// <summary>One Segoe Fluent Icons code: 4-6 hex digits in the private-use range E000-F8FF.</summary>
    private static string Icon(JsonNode? node, string id, List<string> warnings)
    {
        if (node is null) return DefaultIcon;
        if (node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length is >= 4 and <= 6 && s.All(char.IsAsciiHexDigit)
            && Convert.ToInt32(s, 16) is var code and >= 0xE000 and <= 0xF8FF)
            return code.ToString("X4");
        warnings.Add($"\"buttons.{id}.icon\" {Show(node)} isn't an icon code (E000-F8FF); the default icon is used");
        return DefaultIcon;
    }

    private static IReadOnlyDictionary<string, ButtonStyle> ReadOverrides(
        JsonNode? node, IEnumerable<string> shown, List<string> warnings, out IReadOnlyDictionary<string, int> spans)
    {
        var overrides = new Dictionary<string, ButtonStyle>(StringComparer.OrdinalIgnoreCase);
        var picked = new Dictionary<string, int>();
        spans = picked;
        if (node is null) return overrides;
        if (node is not JsonObject all) { warnings.Add("\"overrides\" should be an object; ignored"); return overrides; }
        var ids = shown.ToDictionary(id => id, StringComparer.OrdinalIgnoreCase);
        var seenPickers = new HashSet<string>();
        foreach (var (key, value) in all)
        {
            // A picker takes one key, "span" (its width in button widths), whether or not it is shown.
            if (Pickers.FirstOrDefault(p => p.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } picker)
            {
                if (!seenPickers.Add(picker)) { warnings.Add($"Second override for \"{picker}\" ignored"); continue; }
                if (value is not JsonObject own) { warnings.Add($"Override for \"{picker}\" ignored: the pickers take only \"span\""); continue; }
                if (own.Any(p => p.Key != "span")) warnings.Add($"Override for \"{picker}\" ignored except \"span\": the pickers take no styles");
                if (own.ContainsKey("span") && Span(own["span"], picker, warnings) is { } span && span != DefaultSpan(picker)) picked[picker] = span;
                continue;
            }
            if (!ids.TryGetValue(key, out var id)) { warnings.Add($"Override for \"{Clean(key, MaxLabel, false)}\" ignored: that item isn't shown"); continue; }
            if (overrides.ContainsKey(id)) { warnings.Add($"Second override for \"{id}\" ignored"); continue; }
            if (ReadStyle(value, $"overrides.{id}", warnings) is var style && style != new ButtonStyle()) overrides[id] = style;
        }
        return overrides;
    }

    /// <summary>A picker's span: a whole number 1-8, else a warning and null (the default).</summary>
    private static int? Span(JsonNode? node, string picker, List<string> warnings)
    {
        if (node is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) && d == Math.Floor(d) && d is >= MinSpan and <= MaxSpan)
            return (int)d;
        warnings.Add($"\"overrides.{picker}.span\" {Show(node)} isn't a whole number {MinSpan}-{MaxSpan}; the default is used");
        return null;
    }

    /// <summary>A style object; a bad field is left null (inherit) with a warning, except an unknown size, which is normal.</summary>
    private static ButtonStyle ReadStyle(JsonNode? node, string where, List<string> warnings)
    {
        if (node is null) return new ButtonStyle();
        if (node is not JsonObject o) { warnings.Add($"\"{where}\" should be an object; ignored"); return new ButtonStyle(); }
        ButtonSize? size = null;
        if (o["size"] is { } sizeNode)
        {
            size = sizeNode is JsonValue v && v.TryGetValue<string>(out var s)
                ? Enum.GetValues<ButtonSize>().Cast<ButtonSize?>().FirstOrDefault(z => z.ToString()!.Equals(s, StringComparison.OrdinalIgnoreCase))
                : null;
            if (size is null) warnings.Add($"\"{where}.size\" {Show(sizeNode)} isn't compact, normal or wide; normal is used");
            size ??= ButtonSize.Normal;
        }
        return new ButtonStyle(size, Bool(o["outline"], $"{where}.outline", warnings), Bool(o["filled"], $"{where}.filled", warnings),
            Dip(o["width"], $"{where}.width", warnings), Dip(o["height"], $"{where}.height", warnings), Color(o["outlineColor"], $"{where}.outlineColor", warnings));
    }

    /// <summary>"#RRGGBB" in upper case, or null (the accent colour) for anything else: never a name, markup or a brush.</summary>
    private static string? Color(JsonNode? node, string where, List<string> warnings)
    {
        if (node is null) return null;
        if (node is JsonValue v && v.TryGetValue<string>(out var s) && IsColor(s)) return s.ToUpperInvariant();
        warnings.Add($"\"{where}\" {Show(node)} isn't a #RRGGBB colour; the accent colour is used");
        return null;
    }

    /// <summary>Exactly "#" and six hex digits.</summary>
    internal static bool IsColor(string? s) => s is { Length: 7 } && s[0] == '#' && s.Skip(1).All(char.IsAsciiHexDigit);

    private static bool? Bool(JsonNode? node, string where, List<string> warnings)
    {
        if (node is null) return null;
        if (node is JsonValue v && v.TryGetValue<bool>(out var b)) return b;
        warnings.Add($"\"{where}\" should be true or false, not {Show(node)}; the default is used");
        return null;
    }

    private static int? Dip(JsonNode? node, string where, List<string> warnings)
    {
        if (node is null) return null;
        if (node is not JsonValue v || !v.TryGetValue<double>(out var d) || !double.IsFinite(d))
        {
            warnings.Add($"\"{where}\" should be a number ({MinDip}-{MaxDip}); ignored");
            return null;
        }
        if (d is < MinDip or > MaxDip) warnings.Add($"\"{where}\" {d} is outside {MinDip}-{MaxDip}; clamped");
        return (int)Math.Round(Math.Clamp(d, MinDip, MaxDip));
    }

    /// <summary>Drops control characters (except line breaks and tabs in <paramref name="lines"/> text) and unpaired
    /// surrogates, trims single-line text and cuts to <paramref name="max"/> without splitting a surrogate pair.</summary>
    private static string Clean(string s, int max, bool lines)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[++i]); continue; }
            if (char.IsSurrogate(c) || (char.IsControl(c) && !(lines && c is '\n' or '\r' or '\t'))) continue;
            sb.Append(c);
        }
        var t = lines ? sb.ToString() : sb.ToString().Trim();
        if (t.Length > max) t = t[..(char.IsHighSurrogate(t[max - 1]) ? max - 1 : max)];
        return lines ? t : t.TrimEnd();
    }

    /// <summary>A short, safe rendering of a file value for a warning.</summary>
    private static string Show(JsonNode? node) => node is null ? "null" : Clean(node.ToJsonString(), MaxLabel, false);

    // ---- Writing ----

    private static readonly JsonSerializerOptions WriteOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Str(string s) => JsonSerializer.Serialize(s, WriteOptions);

    private static string StyleText(ButtonStyle s) => "{ " + string.Join(", ", new[]
    {
        s.Size is { } size ? $"\"size\": \"{size.ToString().ToLowerInvariant()}\"" : null,
        s.Outline is { } outline ? $"\"outline\": {(outline ? "true" : "false")}" : null,
        s.Filled is { } filled ? $"\"filled\": {(filled ? "true" : "false")}" : null,
        s.Width is { } w ? $"\"width\": {w}" : null,
        s.Height is { } h ? $"\"height\": {h}" : null,
        s.OutlineColor is { } c ? $"\"outlineColor\": {Str(c)}" : null,
    }.OfType<string>()) + " }";

    private static string ButtonText(CustomButton b) =>
        $"{{ \"label\": {Str(b.Label)}, \"icon\": {Str(b.Icon)}, " + (b.IconFile is { } file ? $"\"iconFile\": {Str(file)}, " : "")
        + (b.QuickAction is { } qa ? $"\"quickAction\": {Str(qa)} }}" : $"\"text\": {Str(b.Text!)}, \"send\": {(b.Send ? "true" : "false")} }}");

    private static string Entries<T>(IEnumerable<(string Key, T Value)> entries, Func<T, string> text) =>
        entries.Any() ? "{\n" + string.Join(",\n", entries.Select(e => $"    {Str(e.Key)}: {text(e.Value)}")) + "\n  }" : "{}";

    /// <summary>The "overrides" entries as written: the items' styles, then the pickers' spans.</summary>
    private IEnumerable<(string Key, string Value)> OverrideTexts() =>
        Overrides.Select(o => (o.Key, StyleText(o.Value)))
            .Concat(Pickers.Where(PickerSpans.ContainsKey).Select(p => (p, "{ \"span\": " + PickerSpans[p] + " }")));

    /// <summary>The model as a v2 file with the comment header (what Settings saves; the renderer never writes).</summary>
    public string ToJson()
    {
        string Row(Quadrant left, Quadrant center, Quadrant right) =>
            $"{{ \"left\": [{string.Join(", ", Items(left).Select(Str))}], \"center\": [{string.Join(", ", Items(center).Select(Str))}], "
            + $"\"right\": [{string.Join(", ", Items(right).Select(Str))}] }}";
        return $$"""
            {
              // The button rows around the message box: "top" above it, "items" below it. Each row has a "left", a "center"
              // and a "right" group, listed left to right (the centre sits in the free space between the other two).
              // Leave an item out to hide it; an item sits in one place only.
              // Built-in items: {{string.Join(", ", BuiltIn)}}, plus the ids under "buttons".
              // The effort, model and provider pickers stay together (where the first one is) and ignore the styles below;
              // their only override is "span", their width in button widths ({{MinSpan}}-{{MaxSpan}}; effort 2, model 4, provider 3).
              // "style": the whole bar; size is compact | normal | wide (half, once, twice the base), outline and filled are true | false,
              // outlineColor is "#RRGGBB" (left out: the accent colour).
              // "overrides": the same keys for one item, plus "width" and "height" ({{MinDip}}-{{MaxDip}}) for icon buttons.
              // Custom buttons stay within {{MaxDip}} x {{MaxDip}} whatever the size.
              // "buttons": up to {{MaxButtons}} custom buttons; id a-z, 0-9 and -, label up to {{MaxLabel}} characters, icon a Segoe
              // Fluent Icons code (E000-F8FF), and exactly one action: "quickAction" (the name of a file in actions\) or
              // "text" (put in the message box; "send": true also sends it). "iconFile": "<id>.png" shows that picture from
              // icons\ instead of the icon (PNG, at most 256 KB, 16-256 px a side; Settings copies it there).
              "version": {{CurrentVersion}},
              "top": {{Row(Quadrant.TopLeft, Quadrant.TopCenter, Quadrant.TopRight)}},
              "items": {{Row(Quadrant.BottomLeft, Quadrant.BottomCenter, Quadrant.BottomRight)}},
              "style": {{StyleText(Style)}},
              "overrides": {{Entries(OverrideTexts(), text => text)}},
              "buttons": {{Entries(Buttons.Select(b => (b.Id, b)), ButtonText)}}
            }
            """;
    }
}
