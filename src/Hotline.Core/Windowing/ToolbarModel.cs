using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Text.Localization;
using Hotline.Core.Settings;

namespace Hotline.Core.Windowing;

/// <summary>Where an item sits. The declaration order decides which place keeps an item listed in two.</summary>
public enum Quadrant { BottomLeft, BottomCenter, BottomRight, TopLeft, TopCenter, TopRight }

/// <summary>The look of the whole bar or of one item; a null field inherits (item, then bar, then the defaults).
/// <see cref="Width"/> is the width in cells (0.5, 1, 2, 3 or 4; a button is always one cell high) and applies to icon
/// buttons only: a picker takes the look (<see cref="Outline"/>, <see cref="OutlineColor"/>, <see cref="Filled"/>) but its
/// width is its span. <see cref="OutlineColor"/> is a validated "#RRGGBB" (upper case) for the outline, null for the accent colour.</summary>
public sealed record ButtonStyle(double? Width = null, bool? Outline = null, bool? Filled = null, string? OutlineColor = null, bool? Fill = null);

/// <summary>A user-defined button: runs the quick action of that name (a file in actions\) or inserts <see cref="Text"/>
/// in the message box (and sends it when <see cref="Send"/>). Exactly one of the two is set. <see cref="IconFile"/> is
/// its picture in icons\ (always "&lt;id&gt;.png", see <see cref="CustomIcons"/>), shown instead of the <see cref="Icon"/>
/// glyph when it can be read; the glyph stays as the fallback. <see cref="Shortcut"/> optionally runs the same action
/// while the panel has focus.</summary>
public sealed record CustomButton(string Id, string Label, string Icon, string? QuickAction, string? Text, bool Send, string? IconFile = null,
    string? Shortcut = null)
{
    /// <summary>The user's binding when runtime validation disabled it. Written back unchanged.</summary>
    public string? RequestedShortcut { get; init; }
}

/// <summary>
/// ~/.hotline/toolbar.json v3, resolved: what each quadrant shows, the bar's look, per-item overrides and the custom buttons.
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
    public const int CurrentVersion = 3;
    /// <summary>A picker is this many button widths wide, at least 1 and at most 8 (see <see cref="EffectiveSpan"/>).</summary>
    public const int MinSpan = 1, MaxSpan = 8;
    /// <summary>The glyph of a custom button whose icon is missing or invalid (Segoe Fluent Icons "FavoriteStar").</summary>
    public const string DefaultIcon = "E734";
    /// <summary>At most this many custom buttons, and items per quadrant (the rest of a longer list is ignored).</summary>
    public const int MaxButtons = 24, MaxItems = 40;
    /// <summary>A custom button id's longest length.</summary>
    public const int MaxId = 32;
    private const int MaxLabel = 40, MaxText = 2000, MaxActionName = 64;

    /// <summary>The widths in cells a button can have (toolbar.json "width"); a cell is <see cref="ToolbarPlan.BaseWidth"/> wide.</summary>
    public static readonly IReadOnlyList<double> CellChoices = [0.5, 1, 2, 3, 4];

    /// <summary>Whether <paramref name="cells"/> is one of <see cref="CellChoices"/>.</summary>
    public static bool IsCells(double cells) => CellChoices.Contains(cells);

    /// <summary>A warning's text in the current language (<see cref="Resolve"/> stays pure: the text is the only thing that varies).</summary>
    private static string W(string format, params object[] args) => L.F(format, args);

    public static readonly IReadOnlyList<string> BuiltIn =
        ["pin", "captureWindow", "captureScreen", "captureRegion", "effort", "model", "provider", "prompt", "recent", "continueIn", "newChat", "settings", "more", "copyLastAnswer"];

    /// <summary>The optional menu button the user places and fills (toolbar.json "menu"); the row's automatic overflow merges into it.</summary>
    public const string MoreId = "more";

    /// <summary>The three pickers. Each is an item like a button: it sits where it is listed, in any area and between any buttons.</summary>
    internal static readonly string[] Pickers = ["effort", "model", "provider"];

    private static readonly Quadrant[] Order = Enum.GetValues<Quadrant>();

    /// <summary>Each picker's width in button widths when the file doesn't say.</summary>
    private static int DefaultSpan(string id) => EmbeddedDefaults.Tree["overrides"]?[id.ToLowerInvariant()]?["span"]?.GetValue<int>()
        ?? (id.ToLowerInvariant() switch { "effort" => 3, "model" => 4, "provider" => 4, _ => 1 });

    /// <summary>The pickers' own widths, in button widths, for the ones that differ from the default (lower-case id to
    /// <see cref="MinSpan"/>-<see cref="MaxSpan"/>). Kept for a picker that is hidden, too.</summary>
    public IReadOnlyDictionary<string, int> PickerSpans { get; init; } = new Dictionary<string, int>();

    /// <summary>The buttons in the <see cref="MoreId"/> button's menu, in order (toolbar.json "menu"): not on the bar, buttons only (never a
    /// picker or the more button), each once. Empty unless a more button is placed (<see cref="Resolve"/> drops the entries otherwise).</summary>
    public IReadOnlyList<string> Menu { get; init; } = [];

    /// <summary>Raw explicit built-in overrides, including invalid values and unknown ids. Null means the key was absent.</summary>
    public JsonObject? Shortcuts { get; init; }
    private IReadOnlyList<string> ShortcutWarnings { get; init; } = [];

    public string ShortcutText(string id) => Shortcuts?.LastOrDefault(p => p.Key.Equals(id, StringComparison.OrdinalIgnoreCase)).Value
        is JsonValue v && v.TryGetValue<string>(out var text) ? text : ChatShortcuts.Defaults.GetValueOrDefault(id, "");

    public Dictionary<string, string> EffectiveShortcuts(Action<string>? warn = null) =>
        ChatShortcuts.Normalize(Shortcuts?.Select(p => new KeyValuePair<string, string>(p.Key,
            p.Value is null ? null! : p.Value is JsonValue v && v.TryGetValue<string>(out var text) ? text : "\0")), warn);

    internal static JsonObject DefaultTree()
        => (JsonObject)EmbeddedDefaults.Tree.DeepClone();

    private static class EmbeddedDefaults
    {
        internal static readonly JsonObject Tree = Read();
        private static JsonObject Read()
        {
            using var stream = typeof(ToolbarModel).Assembly.GetManifestResourceStream("Hotline.Core.Defaults.toolbar.json")!;
            return JsonNode.Parse(stream)!.AsObject();
        }
    }

    /// <summary>The embedded default layout and style.</summary>
    public static ToolbarModel Defaults { get; } = Read(DefaultTree(), []) with { Shortcuts = null };

    /// <summary>"Reset the bar": the default layout and style. Panel shortcuts (also in toolbar.json) are kept.</summary>
    public static ToolbarModel ResetLayout(ToolbarModel? current) => Defaults with { Shortcuts = current?.Shortcuts };

    private static IReadOnlyDictionary<Quadrant, IReadOnlyList<string>> Layout(Func<Quadrant, IReadOnlyList<string>> items) =>
        Order.ToDictionary(q => q, items);

    public IReadOnlyList<string> Items(Quadrant quadrant) => Placed.TryGetValue(quadrant, out var items) ? items : [];

    /// <summary>The style of one item, per field: its override, else the bar's style, else the defaults (the width: override, else
    /// bar, else 1 cell). A picker gets the look only, its width is null (a picker's width is its span).</summary>
    public ButtonStyle EffectiveStyle(string id)
    {
        var o = Overrides.GetValueOrDefault(id);
        var picker = Pickers.Contains(id, StringComparer.OrdinalIgnoreCase);
        return new ButtonStyle(
            picker ? null : o?.Width ?? Style.Width ?? Defaults.Style.Width,
            o?.Outline ?? Style.Outline ?? Defaults.Style.Outline,
            o?.Filled ?? Style.Filled ?? Defaults.Style.Filled,
            o?.OutlineColor ?? Style.OutlineColor);
    }

    /// <summary>How many button widths a picker is wide: its span, else its embedded default (effort 3, model 4, provider 5), clamped to
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
        && PickerSpans.All(p => other.PickerSpans.TryGetValue(p.Key, out var n) && n == p.Value)
        && Menu.SequenceEqual(other.Menu)
        && JsonNode.DeepEquals(Shortcuts ?? new JsonObject(), other.Shortcuts ?? new JsonObject());

    public override int GetHashCode() =>
        HashCode.Combine(Style, Overrides.Count, Buttons.Count, string.Join(",", Order.SelectMany(Items)));

    // ---- Reading ----

    private static readonly JsonDocumentOptions ParseOptions =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, AllowDuplicateProperties = false };
    private static readonly JsonDocumentOptions RepeatedKeys = ParseOptions with { AllowDuplicateProperties = true };

    /// <summary>Reads toolbar.json text. Never throws: unparsable text or a root that isn't an object gives
    /// <see cref="Defaults"/>; anything else is fixed per field. Stop and built-in shortcut conflicts
    /// are checked too. All bindings come from this toolbar's map.</summary>
    public static ToolbarModel Resolve(string json)
    {
        try
        {
            var warnings = new List<string>();
            var root = Parse(json, warnings);
            return root is JsonObject o ? Resolve(o, warnings) : Defaults with { Warnings = [W(Strings.BarW_NotObject)] };
        }
        catch (Exception ex) when (ex is JsonException or InvalidCastException or InvalidOperationException)
        {
            return Defaults with { Warnings = [W(Strings.BarW_CantRead, Clean(ex.Message, 200, false))] };
        }
    }

    internal static ToolbarModel Resolve(JsonObject root, List<string> warnings)
    {
        try
        {
            return Read(root, warnings).ResolveShortcuts();
        }
        catch (Exception ex) when (ex is JsonException or InvalidCastException or InvalidOperationException)
        {
            return Defaults with { Warnings = [W(Strings.BarW_CantRead, Clean(ex.Message, 200, false))] };
        }
    }

    /// <summary>Resolves conflicts against the toolbar commands without changing the requested bindings.</summary>
    public ToolbarModel ResolveShortcuts()
    {
        var warnings = Warnings.Except(ShortcutWarnings).ToList();
        var sourceCount = warnings.Count;
        var effective = EffectiveShortcuts(warnings.Add);
        var buttons = ChatShortcuts.NormalizeCustom(Buttons, effective, warnings.Add);
        return this with { Buttons = buttons, Warnings = warnings, ShortcutWarnings = warnings.Skip(sourceCount).ToList() };
    }

    /// <summary>The tolerant read shared by <see cref="Resolve"/> and <see cref="ToolbarFile.Load"/>: strips a BOM, accepts comments and
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
            warnings.Add(W(Strings.BarW_Repeated, Clean(strict.Message, 200, false)));
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
                foreach (var p in e.EnumerateObject())
                {
                    o.Remove(p.Name); // The surviving value occupies its last position for shortcut conflict order.
                    o[p.Name] = LastWins(p.Value);
                }
                return o;
            case JsonValueKind.Array:
                return new JsonArray([.. e.EnumerateArray().Select(LastWins)]);
            default:
                return JsonValue.Create(e);
        }
    }

    private static ToolbarModel Read(JsonObject root, List<string> warnings)
    {
        int? version = root["version"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;
        if (version is null || version < CurrentVersion)
            return Defaults with { Warnings = [Strings.Bar_OldVersionUnsupported] };
        if (version != CurrentVersion)
            warnings.Add(W(Strings.BarW_Version, Show(root["version"]), CurrentVersion));

        var shortcuts = ReadShortcuts(root, warnings);
        var defaults = DefaultTree();
        var input = (JsonObject)root.DeepClone();
        void Inherit(JsonObject target, JsonObject source, bool ids = false)
        {
            foreach (var (key, value) in source)
            {
                var own = ids ? target.Select(p => p.Key).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key : key;
                if (target[own] is null) target[own] = value?.DeepClone();
                else if (target[own] is JsonObject child && value is JsonObject parent) Inherit(child, parent, key is "shortcuts" or "overrides" or "buttons");
            }
        }
        Inherit(input, defaults);
        root = input;
        var buttons = ReadButtons(root["buttons"], warnings);
        var known = BuiltIn.Concat(buttons.Select(b => b.Id)).ToDictionary(id => id, StringComparer.OrdinalIgnoreCase);
        var (placed, explicitlyEmpty) = ReadQuadrants(root, known, warnings);
        var layoutDefaulted = false;
        if (placed.Values.All(l => l.Count == 0) && !explicitlyEmpty)
        {
            warnings.Add(W(Strings.BarW_NoItems));
            placed = Order.ToDictionary(q => q, q => Defaults.Items(q).ToList());
            layoutDefaulted = true;
        }

        var menu = ReadMenu(root["menu"], known, placed, warnings);
        var style = ReadStyle(root["style"], "style", warnings);
        var overrides = ReadOverrides(root["overrides"], known.Keys, warnings, out var spans);
        // A defaulted layout also takes the embedded overrides when none were set.
        if (layoutDefaulted && overrides.Count == 0 && spans.Count == 0) (overrides, spans) = (Defaults.Overrides, Defaults.PickerSpans);
        return new ToolbarModel(
            Layout(q => placed[q]),
            style with { Width = style.Width ?? defaults["style"]!["width"]!.GetValue<double>(), Outline = style.Outline ?? defaults["style"]!["outline"]!.GetValue<bool>(), Filled = style.Filled ?? defaults["style"]!["filled"]!.GetValue<bool>(), Fill = style.Fill ?? defaults["style"]!["fill"]!.GetValue<bool>() },
            overrides,
            buttons,
            warnings) { PickerSpans = spans, Menu = menu, Shortcuts = shortcuts };
    }

    private static JsonObject? ReadShortcuts(JsonObject root, List<string> warnings)
    {
        if (root["shortcuts"] is null) return null;
        if (root["shortcuts"] is JsonObject map) return ReadShortcutMap(map, warnings);
        warnings.Add(W(Strings.BarW_Object, "shortcuts"));
        return new JsonObject();
    }

    internal static JsonObject ReadShortcutMap(JsonObject map, List<string> warnings)
    {
        var result = new JsonObject();
        foreach (var (name, value) in map)
        {
            var id = BuiltIn.Prepend("stop").FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
            var previous = result.Select(p => p.Key).FirstOrDefault(k => k.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (previous is not null)
            {
                warnings.Add(W(Strings.Shortcuts_Duplicate, id));
                result.Remove(previous);
            }
            result[id] = value?.DeepClone();
        }
        return result;
    }

    private static (Dictionary<Quadrant, List<string>> Placed, bool ExplicitlyEmpty) ReadQuadrants(
        JsonObject root, Dictionary<string, string> known, List<string> warnings)
    {
        var placed = Order.ToDictionary(q => q, _ => new List<string>());
        var seen = new HashSet<string>();
        var explicitlyEmpty = true;
        foreach (var rowName in new[] { "bottom", "top" })
            if (root[rowName] is { } row && row is not JsonObject)
                warnings.Add(W(Strings.BarW_RowObject, rowName));
        foreach (var q in Order)
        {
            var rowName = q is Quadrant.BottomLeft or Quadrant.BottomCenter or Quadrant.BottomRight ? "bottom" : "top";
            var side = q is Quadrant.BottomLeft or Quadrant.TopLeft ? "left" : q is Quadrant.BottomCenter or Quadrant.TopCenter ? "center" : "right";
            if ((root[rowName] as JsonObject)?[side] is not JsonArray list)
            {
                if (side != "center") explicitlyEmpty = false; // the centre group is optional: a file without one is still an explicit empty bar
                if ((root[rowName] as JsonObject)?[side] is not null) warnings.Add(W(Strings.BarW_List, rowName + "." + side));
                continue;
            }
            if (list.Count > 0) explicitlyEmpty = false;
            if (list.Count > MaxItems) warnings.Add(W(Strings.BarW_TooMany, rowName + "." + side, MaxItems));
            foreach (var entry in list.Take(MaxItems))
            {
                if (entry is not JsonValue value || !value.TryGetValue<string>(out var s)) { warnings.Add(W(Strings.BarW_NotItemId, rowName + "." + side, Show(entry))); continue; }
                if (!known.TryGetValue(s, out var id)) warnings.Add(W(Strings.BarW_Unknown, Clean(s, MaxLabel, false)));
                else if (seen.Add(id)) placed[q].Add(id); // listed again later: the first place wins
            }
        }

        return (placed, explicitlyEmpty);
    }

    /// <summary>The "menu" list: buttons only, each once, none that is on the bar; all ignored (one warning) when no more button is placed.</summary>
    private static List<string> ReadMenu(JsonNode? node, Dictionary<string, string> known, Dictionary<Quadrant, List<string>> placed, List<string> warnings)
    {
        var menu = new List<string>();
        if (node is null) return menu;
        if (node is not JsonArray list) { warnings.Add(W(Strings.BarW_List, "menu")); return menu; }
        if (list.Count > MaxItems) warnings.Add(W(Strings.BarW_TooMany, "menu", MaxItems));
        var onBar = placed.Values.SelectMany(l => l).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in list.Take(MaxItems))
        {
            if (entry is not JsonValue value || !value.TryGetValue<string>(out var s)) { warnings.Add(W(Strings.BarW_NotItemId, "menu", Show(entry))); continue; }
            if (!known.TryGetValue(s, out var id)) warnings.Add(W(Strings.BarW_UnknownMenu, Clean(s, MaxLabel, false)));
            else if (Pickers.Contains(id, StringComparer.OrdinalIgnoreCase) || id.Equals(MoreId, StringComparison.OrdinalIgnoreCase))
                warnings.Add(W(Strings.BarW_MenuButtonsOnly, id));
            else if (onBar.Contains(id)) warnings.Add(W(Strings.BarW_MenuOnBar, id));
            else if (menu.Contains(id, StringComparer.OrdinalIgnoreCase)) warnings.Add(W(Strings.BarW_MenuTwice, id));
            else menu.Add(id);
        }
        if (menu.Count > 0 && !onBar.Contains(MoreId))
        {
            warnings.Add(W(Strings.BarW_MenuNoMore));
            menu.Clear();
        }
        return menu;
    }

    private static List<CustomButton> ReadButtons(JsonNode? node, List<string> warnings)
    {
        var buttons = new List<CustomButton>();
        if (node is null) return buttons;
        if (node is not JsonObject all) { warnings.Add(W(Strings.BarW_Object, "buttons")); return buttons; }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, def) in all)
        {
            if (buttons.Count == MaxButtons) { warnings.Add(W(Strings.BarW_TooManyButtons, MaxButtons)); break; }
            var name = Clean(id, MaxLabel, false);
            if (!IsCustomId(id))
                { warnings.Add(W(Strings.BarW_ButtonId, name, MaxId)); continue; }
            if (BuiltIn.Contains(id, StringComparer.OrdinalIgnoreCase)) { warnings.Add(W(Strings.BarW_ButtonBuiltIn, name)); continue; }
            if (!seen.Add(id)) { warnings.Add(W(Strings.Bar_DuplicateButton, name)); continue; }
            if (def is not JsonObject b) { warnings.Add(W(Strings.BarW_ButtonObject, id)); continue; }

            var label = Clean(String(b, "label", id, warnings) ?? "", MaxLabel, false);
            var quickAction = String(b, "quickAction", id, warnings) is { } qa ? Clean(qa, int.MaxValue, false) : null;
            if (quickAction is not null && !IsActionName(quickAction))
                { warnings.Add(W(Strings.BarW_ButtonAction, id, MaxActionName)); continue; }
            var text = String(b, "text", id, warnings) is { } t && Clean(t, MaxText, true) is { Length: > 0 } x ? x : null;
            if ((quickAction is null) == (text is null)) { warnings.Add(W(Strings.BarW_ButtonOneOf, id)); continue; }
            // Only the name is checked here (Resolve reads no files): the renderer falls back to the glyph if the picture is gone.
            var iconFile = String(b, "iconFile", id, warnings);
            if (iconFile is not null && !CustomIcons.IsFileName(iconFile, id))
            {
                warnings.Add(W(Strings.BarW_ButtonIconFile, id, Show(b["iconFile"])));
                iconFile = null;
            }
            buttons.Add(new CustomButton(id, label.Length > 0 ? label : id, Icon(b["icon"], id, warnings), quickAction, text,
                text is not null && (Bool(b["send"], $"buttons.{id}.send", warnings) ?? false), iconFile,
                String(b, "shortcut", id, warnings)));
        }
        return buttons;
    }

    /// <summary>The form of a custom button id: 1-32 ASCII letters, digits or - (built-in collisions are checked apart).
    /// "stop" and "spacer" are reserved.</summary>
    internal static bool IsCustomId(string? id) =>
        id is { Length: > 0 and <= MaxId } && id.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-')
        && !id.Equals("spacer", StringComparison.OrdinalIgnoreCase) && !id.Equals("stop", StringComparison.OrdinalIgnoreCase);

    /// <summary>The name of a file in actions\ as QuickActions reads and parses it (never a path), at most 64 characters.</summary>
    internal static bool IsActionName(string name) => name.Length <= MaxActionName && Chat.QuickActions.IsName(name);

    private static string? String(JsonObject o, string key, string id, List<string> warnings)
    {
        if (o[key] is null) return null;
        if (o[key] is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        warnings.Add(W(Strings.BarW_ButtonText, $"buttons.{id}.{key}"));
        return null;
    }

    /// <summary>One Segoe Fluent Icons code: 4-6 hex digits in the private-use range E000-F8FF.</summary>
    private static string Icon(JsonNode? node, string id, List<string> warnings)
    {
        if (node is null) return DefaultIcon;
        if (node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length is >= 4 and <= 6 && s.All(char.IsAsciiHexDigit)
            && Convert.ToInt32(s, 16) is var code and >= 0xE000 and <= 0xF8FF)
            return code.ToString("X4");
        warnings.Add(W(Strings.BarW_ButtonIcon, $"buttons.{id}.icon", Show(node)));
        return DefaultIcon;
    }

    private static IReadOnlyDictionary<string, ButtonStyle> ReadOverrides(
        JsonNode? node, IEnumerable<string> knownIds, List<string> warnings, out IReadOnlyDictionary<string, int> spans)
    {
        var overrides = new Dictionary<string, ButtonStyle>(StringComparer.OrdinalIgnoreCase);
        var picked = new Dictionary<string, int>();
        spans = picked;
        if (node is null) return overrides;
        if (node is not JsonObject all) { warnings.Add(W(Strings.BarW_Object, "overrides")); return overrides; }
        var ids = knownIds.ToDictionary(id => id, StringComparer.OrdinalIgnoreCase);
        var seenPickers = new HashSet<string>();
        foreach (var (key, value) in all)
        {
            // A picker takes "span" (its width in button widths) and the look keys, whether or not it is shown.
            if (Pickers.FirstOrDefault(p => p.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } picker)
            {
                if (!seenPickers.Add(picker)) { warnings.Add(W(Strings.BarW_OverrideTwice, picker)); continue; }
                if (value is not JsonObject own) { warnings.Add(W(Strings.BarW_PickerSpanOnly, picker)); continue; }
                if (own.ContainsKey("width")) warnings.Add(W(Strings.BarW_PickerNoStyles, picker));
                if (own["span"] is not null && Span(own["span"], picker, warnings) is { } span && span != DefaultSpan(picker)) picked[picker] = span;
                var look = new ButtonStyle(null, Bool(own["outline"], $"overrides.{picker}.outline", warnings), Bool(own["filled"], $"overrides.{picker}.filled", warnings),
                    Color(own["outlineColor"], $"overrides.{picker}.outlineColor", warnings));
                if (look != new ButtonStyle()) overrides[picker] = look;
                continue;
            }
            if (!ids.TryGetValue(key, out var id)) { warnings.Add(W(Strings.BarW_OverrideNotShown, Clean(key, MaxLabel, false))); continue; }
            if (overrides.ContainsKey(id)) { warnings.Add(W(Strings.BarW_OverrideTwice, id)); continue; }
            if (ReadStyle(value, $"overrides.{id}", warnings) is var style && style != new ButtonStyle()) overrides[id] = style;
        }
        return overrides;
    }

    /// <summary>A picker's span: a whole number 1-8, else a warning and null (the default).</summary>
    private static int? Span(JsonNode? node, string picker, List<string> warnings)
    {
        if (node is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) && d == Math.Floor(d) && d is >= MinSpan and <= MaxSpan)
            return (int)d;
        warnings.Add(W(Strings.BarW_SpanBad, $"overrides.{picker}.span", Show(node), MinSpan, MaxSpan));
        return null;
    }

    /// <summary>A style object; a bad field is left null (inherit) with a warning. The width is a number: 0.5, 1, 2, 3 or 4.</summary>
    private static ButtonStyle ReadStyle(JsonNode? node, string where, List<string> warnings)
    {
        if (node is null) return new ButtonStyle();
        if (node is not JsonObject o) { warnings.Add(W(Strings.BarW_Object, where)); return new ButtonStyle(); }
        return new ButtonStyle(ReadWidth(o, where, warnings), Bool(o["outline"], $"{where}.outline", warnings), Bool(o["filled"], $"{where}.filled", warnings),
            Color(o["outlineColor"], $"{where}.outlineColor", warnings), where == "style" ? Bool(o["fill"], "style.fill", warnings) : null);
    }

    private static double? ReadWidth(JsonObject o, string where, List<string> warnings)
    {
        if (o["width"] is null) return null;
        if (o["width"] is JsonValue v && v.TryGetValue<double>(out var width) && IsCells(width)) return width;
        warnings.Add(W(Strings.Bar_WidthInvalid, where + ".width", Show(o["width"])));
        return null;
    }

    /// <summary>"#RRGGBB" in upper case, or null (the accent colour) for anything else: never a name, markup or a brush.</summary>
    private static string? Color(JsonNode? node, string where, List<string> warnings)
    {
        if (node is null) return null;
        if (node is JsonValue v && v.TryGetValue<string>(out var s) && IsColor(s)) return s.ToUpperInvariant();
        warnings.Add(W(Strings.BarW_ColorBad, where, Show(node)));
        return null;
    }

    /// <summary>Exactly "#" and six hex digits.</summary>
    internal static bool IsColor(string? s) => s is { Length: 7 } && s[0] == '#' && s.Skip(1).All(char.IsAsciiHexDigit);

    private static bool? Bool(JsonNode? node, string where, List<string> warnings)
    {
        if (node is null) return null;
        if (node is JsonValue v && v.TryGetValue<bool>(out var b)) return b;
        warnings.Add(W(Strings.BarW_BoolBad, where, Show(node)));
        return null;
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

    /// <summary>The style fields, with numeric widths; invalid widths are left out.</summary>
    private static IEnumerable<string> StyleParts(ButtonStyle s)
    {
        if (s.Width is { } w && IsCells(w))
            yield return "\"width\": " + w.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        if (s.Fill is { } fill) yield return $"\"fill\": {(fill ? "true" : "false")}";
        if (s.Outline is { } outline) yield return $"\"outline\": {(outline ? "true" : "false")}";
        if (s.Filled is { } filled) yield return $"\"filled\": {(filled ? "true" : "false")}";
        if (s.OutlineColor is { } c) yield return $"\"outlineColor\": {Str(c)}";
    }

    private static string StyleText(ButtonStyle s) => "{ " + string.Join(", ", StyleParts(s)) + " }";

    private static string ButtonText(CustomButton b) =>
        $"{{ \"label\": {Str(b.Label)}, \"icon\": {Str(b.Icon)}, " + (b.IconFile is { } file ? $"\"iconFile\": {Str(file)}, " : "")
        + ((b.RequestedShortcut ?? b.Shortcut) is { Length: > 0 } shortcut ? $"\"shortcut\": {Str(shortcut)}, " : "")
        + (b.QuickAction is { } qa ? $"\"quickAction\": {Str(qa)} }}" : $"\"text\": {Str(b.Text!)}, \"send\": {(b.Send ? "true" : "false")} }}");

    private static string Entries<T>(IEnumerable<(string Key, T Value)> entries, Func<T, string> text) =>
        entries.Any() ? "{\n" + string.Join(",\n", entries.Select(e => $"    {Str(e.Key)}: {text(e.Value)}")) + "\n  }" : "{}";

    /// <summary>The "overrides" entries as written: the buttons' styles, then the pickers' span and look (no width).</summary>
    private IEnumerable<(string Key, string Value)> OverrideTexts() =>
        Overrides.Where(o => !Pickers.Contains(o.Key, StringComparer.OrdinalIgnoreCase)).Select(o => (o.Key, StyleText(o.Value)))
            .Concat(Pickers.Select(p => (Key: p, Parts: PickerParts(p))).Where(p => p.Parts.Count > 0).Select(p => (p.Key, "{ " + string.Join(", ", p.Parts) + " }")));

    private List<string> PickerParts(string picker) =>
    [
        .. PickerSpans.TryGetValue(picker, out var n) && n != DefaultSpan(picker) ? [$"\"span\": {n}"] : Array.Empty<string>(),
        .. Overrides.TryGetValue(picker, out var look) ? StyleParts(look with { Width = null }) : [],
    ];

    /// <summary>The model as a v3 file (what Settings saves; the renderer never writes).</summary>
    public string ToJson()
    {
        string Row(Quadrant left, Quadrant center, Quadrant right) =>
            $"{{ \"left\": [{string.Join(", ", Items(left).Select(Str))}], \"center\": [{string.Join(", ", Items(center).Select(Str))}], "
            + $"\"right\": [{string.Join(", ", Items(right).Select(Str))}] }}";
        return $$"""
            {
              "version": {{CurrentVersion}},
              "shortcuts": {{Shortcuts?.ToJsonString(WriteOptions) ?? "{}"}},
              "top": {{Row(Quadrant.TopLeft, Quadrant.TopCenter, Quadrant.TopRight)}},
              "bottom": {{Row(Quadrant.BottomLeft, Quadrant.BottomCenter, Quadrant.BottomRight)}},
              "menu": [{{string.Join(", ", Menu.Select(Str))}}],
              "style": {{StyleText(Style)}},
              "overrides": {{Entries(OverrideTexts(), text => text)}},
              "buttons": {{Entries(Buttons.Select(b => (b.Id, b)), ButtonText)}}
            }
            """;
    }
}
