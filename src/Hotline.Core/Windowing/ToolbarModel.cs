using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Windowing;

/// <summary>Where an item sits. The declaration order decides which place keeps an item listed in two.</summary>
public enum Quadrant { BottomLeft, BottomCenter, BottomRight, TopLeft, TopCenter, TopRight }

/// <summary>The look of the whole bar or of one item; a null field inherits (item, then bar, then the defaults).
/// <see cref="Width"/> is the width in cells (0.5, 1, 2, 3 or 4; a button is always one cell high) and applies to icon
/// buttons only: a picker takes the look (<see cref="Outline"/>, <see cref="OutlineColor"/>, <see cref="Filled"/>) but its
/// width is its span. <see cref="OutlineColor"/> is a validated "#RRGGBB" (upper case) for the outline, null for the accent colour.</summary>
public sealed record ButtonStyle(double? Width = null, bool? Outline = null, bool? Filled = null, string? OutlineColor = null);

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
    private const int MaxLabel = 40, MaxText = 2000, MaxActionName = 64;

    /// <summary>The widths in cells a button can have (toolbar.json "width"); a cell is <see cref="ToolbarPlan.BaseWidth"/> wide.</summary>
    public static readonly IReadOnlyList<double> CellChoices = [0.5, 1, 2, 3, 4];

    /// <summary>Whether <paramref name="cells"/> is one of <see cref="CellChoices"/>.</summary>
    public static bool IsCells(double cells) => CellChoices.Contains(cells);

    /// <summary>The words that stand for a width: compact 0.5, normal 1, wide 2 cells.</summary>
    private static readonly (string Word, double Cells)[] WidthWords = [("compact", 0.5), ("normal", 1), ("wide", 2)];

    /// <summary>The word of a width (compact, normal, wide) or null for 3 and 4 cells and for anything else.</summary>
    public static string? WidthWord(double cells) => WidthWords.FirstOrDefault(w => w.Cells == cells).Word;

    /// <summary>A warning's text in the current language (<see cref="Resolve"/> stays pure: the text is the only thing that varies).</summary>
    private static string W(string format, params object[] args) => L.F(format, args);

    public static readonly IReadOnlyList<string> BuiltIn =
        ["pin", "captureWindow", "captureScreen", "captureRegion", "effort", "model", "provider", "prompt", "recent", "continueIn", "newChat", "settings", "more"];

    /// <summary>The optional menu button the user places and fills (toolbar.json "menu"); the row's automatic overflow merges into it.</summary>
    public const string MoreId = "more";

    /// <summary>The three pickers. Each is an item like a button: it sits where it is listed, in any area and between any buttons.</summary>
    internal static readonly string[] Pickers = ["effort", "model", "provider"];

    private static readonly Quadrant[] Order = Enum.GetValues<Quadrant>();

    /// <summary>Each picker's width in button widths when the file doesn't say.</summary>
    private static int DefaultSpan(string id) => id.ToLowerInvariant() switch { "effort" => 3, "model" => 4, "provider" => 4, _ => 1 };

    /// <summary>The pickers' own widths, in button widths, for the ones that differ from the default (lower-case id to
    /// <see cref="MinSpan"/>-<see cref="MaxSpan"/>). Kept for a picker that is hidden, too.</summary>
    public IReadOnlyDictionary<string, int> PickerSpans { get; init; } = new Dictionary<string, int>();

    /// <summary>The buttons in the <see cref="MoreId"/> button's menu, in order (toolbar.json "menu"): not on the bar, buttons only (never a
    /// picker or the more button), each once. Empty unless a more button is placed (<see cref="Resolve"/> drops the entries otherwise).</summary>
    public IReadOnlyList<string> Menu { get; init; } = [];

    /// <summary>The shipped layout, the owner's own (2026-11-01): the three pickers across the top row (effort 3, model 4, provider 5
    /// cells wide); capture buttons, prompt and continue-in, and recent / new chat / settings (two cells wide each) in the bottom row.</summary>
    public static ToolbarModel Defaults { get; } = new(
        Layout(q => q switch
        {
            Quadrant.BottomLeft => ["pin", "captureWindow", "captureScreen", "captureRegion"],
            Quadrant.BottomCenter => ["prompt", "continueIn"],
            Quadrant.BottomRight => ["recent", "newChat", "settings"],
            Quadrant.TopLeft => ["effort"],
            Quadrant.TopCenter => ["model"],
            Quadrant.TopRight => ["provider"],
            _ => [],
        }),
        new ButtonStyle(1, false, false),
        new Dictionary<string, ButtonStyle>(StringComparer.OrdinalIgnoreCase)
        {
            ["recent"] = new ButtonStyle(2), ["newChat"] = new ButtonStyle(2), ["settings"] = new ButtonStyle(2), // two cells across
        },
        [],
        [])
    { PickerSpans = new Dictionary<string, int> { ["provider"] = 5 } };

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

    /// <summary>How many button widths a picker is wide: its span, else its default (effort 3, model 4, provider 4), clamped to
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
        && Menu.SequenceEqual(other.Menu);

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
            return root is JsonObject o ? Read(o, warnings) : Defaults with { Warnings = [W(Strings.BarW_NotObject)] };
        }
        catch (Exception ex) when (ex is JsonException or InvalidCastException or InvalidOperationException)
        {
            return Defaults with { Warnings = [W(Strings.BarW_CantRead, Clean(ex.Message, 200, false))] };
        }
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
                foreach (var p in e.EnumerateObject()) o[p.Name] = LastWins(p.Value);
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
        // The old format (no version or an older one, rows as plain arrays) is not converted any more: the defaults run and
        // the file is left as it is. A version 2 file with a bad row is read per field like any other.
        if ((root["version"] is null || version < CurrentVersion) && (root["items"] is JsonArray || root["top"] is JsonArray))
            return Defaults with { Warnings = [Strings.Bar_VersionOneUnsupported] };
        if (root["version"] is not null && version != CurrentVersion)
            warnings.Add(W(Strings.BarW_Version, Show(root["version"]), CurrentVersion));

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
        var overrides = ReadOverrides(root["overrides"], placed.Values.SelectMany(l => l), warnings, out var spans);
        // A bar that fell back to the shipped layout also gets the shipped look of it (its wide buttons, the provider's width), unless the file set its own.
        if (layoutDefaulted && overrides.Count == 0 && spans.Count == 0) (overrides, spans) = (Defaults.Overrides, Defaults.PickerSpans);
        return new ToolbarModel(
            Layout(q => placed[q]),
            style with { Width = style.Width ?? 1, Outline = style.Outline ?? false, Filled = style.Filled ?? false },
            overrides,
            buttons,
            warnings) { PickerSpans = spans, Menu = menu };
    }

    private static (Dictionary<Quadrant, List<string>> Placed, bool ExplicitlyEmpty) ReadQuadrants(
        JsonObject root, Dictionary<string, string> known, List<string> warnings)
    {
        var placed = Order.ToDictionary(q => q, _ => new List<string>());
        var seen = new HashSet<string>();
        var explicitlyEmpty = true;
        foreach (var rowName in new[] { "items", "top" })
            if (root[rowName] is { } row && row is not JsonObject)
                warnings.Add(W(Strings.BarW_RowObject, rowName));
        foreach (var q in Order)
        {
            var rowName = q is Quadrant.BottomLeft or Quadrant.BottomCenter or Quadrant.BottomRight ? "items" : "top";
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
                if (s.Equals("spacer", StringComparison.OrdinalIgnoreCase)) continue; // v1 leftover: groups replace it
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
        foreach (var (id, def) in all)
        {
            if (buttons.Count == MaxButtons) { warnings.Add(W(Strings.BarW_TooManyButtons, MaxButtons)); break; }
            var name = Clean(id, MaxLabel, false);
            if (!IsCustomId(id))
                { warnings.Add(W(Strings.BarW_ButtonId, name, MaxId)); continue; }
            if (BuiltIn.Contains(id, StringComparer.OrdinalIgnoreCase)) { warnings.Add(W(Strings.BarW_ButtonBuiltIn, name)); continue; }
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
        JsonNode? node, IEnumerable<string> shown, List<string> warnings, out IReadOnlyDictionary<string, int> spans)
    {
        var overrides = new Dictionary<string, ButtonStyle>(StringComparer.OrdinalIgnoreCase);
        var picked = new Dictionary<string, int>();
        spans = picked;
        if (node is null) return overrides;
        if (node is not JsonObject all) { warnings.Add(W(Strings.BarW_Object, "overrides")); return overrides; }
        var ids = shown.ToDictionary(id => id, StringComparer.OrdinalIgnoreCase);
        var seenPickers = new HashSet<string>();
        foreach (var (key, value) in all)
        {
            // A picker takes "span" (its width in button widths) and the look keys, whether or not it is shown.
            if (Pickers.FirstOrDefault(p => p.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } picker)
            {
                if (!seenPickers.Add(picker)) { warnings.Add(W(Strings.BarW_OverrideTwice, picker)); continue; }
                if (value is not JsonObject own) { warnings.Add(W(Strings.BarW_PickerSpanOnly, picker)); continue; }
                if (own.Any(p => p.Key is not ("span" or "outline" or "outlineColor" or "filled"))) warnings.Add(W(Strings.BarW_PickerNoStyles, picker));
                if (own.ContainsKey("span") && Span(own["span"], picker, warnings) is { } span && span != DefaultSpan(picker)) picked[picker] = span;
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

    /// <summary>A style object; a bad field is left null (inherit) with a warning. The width is <c>width</c> (a number 0.5, 1, 2, 3 or 4, or
    /// the word compact, normal or wide); <c>size</c> (the words) and <c>cells</c> (the numbers) are read as its old names, silently, but
    /// <c>width</c> wins when both are there (one warning).</summary>
    private static ButtonStyle ReadStyle(JsonNode? node, string where, List<string> warnings)
    {
        if (node is null) return new ButtonStyle();
        if (node is not JsonObject o) { warnings.Add(W(Strings.BarW_Object, where)); return new ButtonStyle(); }
        if (o.ContainsKey("height")) { var gone = W(Strings.BarW_WidthHeightGone); if (!warnings.Contains(gone)) warnings.Add(gone); }
        return new ButtonStyle(ReadWidth(o, where, warnings), Bool(o["outline"], $"{where}.outline", warnings), Bool(o["filled"], $"{where}.filled", warnings),
            Color(o["outlineColor"], $"{where}.outlineColor", warnings));
    }

    private static double? ReadWidth(JsonObject o, string where, List<string> warnings)
    {
        if (o["width"] is { } widthNode)
        {
            if (WidthOf(widthNode, true, true) is { } w)
            {
                if (o.ContainsKey("size") || o.ContainsKey("cells")) warnings.Add(W(Strings.BarW_WidthAlias, where));
                return w;
            }
            warnings.Add(W(Strings.BarW_WidthBad, where + ".width", Show(widthNode)));
        }
        // The old names: cells (numbers) won over size (words) when an item had both.
        double? result = null;
        if (o["cells"] is { } cellsNode)
        {
            result = WidthOf(cellsNode, false, true);
            if (result is null) warnings.Add(W(Strings.BarW_CellsBad, where + ".cells", Show(cellsNode)));
        }
        if (o["size"] is { } sizeNode)
        {
            var size = WidthOf(sizeNode, true, false);
            if (size is null) warnings.Add(W(Strings.BarW_SizeBad, where + ".size", Show(sizeNode)));
            result ??= size;
        }
        return result;
    }

    /// <summary>A width in cells from a JSON value: one of <see cref="CellChoices"/> as a number, or compact, normal or wide as a word; null otherwise.</summary>
    private static double? WidthOf(JsonNode node, bool words, bool numbers)
    {
        if (node is not JsonValue v) return null;
        if (numbers && v.TryGetValue<double>(out var d) && IsCells(d)) return d;
        if (words && v.TryGetValue<string>(out var s) && WidthWords.FirstOrDefault(x => x.Word.Equals(s, StringComparison.OrdinalIgnoreCase)) is { Word: not null } hit) return hit.Cells;
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

    /// <summary>The key-value texts of a style: the width as its word for 0.5, 1 and 2 cells and as a number for 3 and 4 (a width that is none of them is left out).</summary>
    private static IEnumerable<string> StyleParts(ButtonStyle s)
    {
        if (s.Width is { } w && IsCells(w))
            yield return "\"width\": " + (WidthWord(w) is { } word ? $"\"{word}\"" : w.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        if (s.Outline is { } outline) yield return $"\"outline\": {(outline ? "true" : "false")}";
        if (s.Filled is { } filled) yield return $"\"filled\": {(filled ? "true" : "false")}";
        if (s.OutlineColor is { } c) yield return $"\"outlineColor\": {Str(c)}";
    }

    private static string StyleText(ButtonStyle s) => "{ " + string.Join(", ", StyleParts(s)) + " }";

    private static string ButtonText(CustomButton b) =>
        $"{{ \"label\": {Str(b.Label)}, \"icon\": {Str(b.Icon)}, " + (b.IconFile is { } file ? $"\"iconFile\": {Str(file)}, " : "")
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
              // The effort, model and provider pickers are items like any other (each can sit in any area, between any buttons).
              // They take the bar's look (outline, outlineColor, filled) but not its width: their width is the "span" override, in button
              // widths ({{MinSpan}}-{{MaxSpan}}; effort 3, model 4, provider 4; a span equal to that is not written).
              // "style": the whole bar. "width" is the width of a button in cells (0.5, 1, 2, 3 or 4; every button is one cell high);
              // the words compact, normal and wide are shortcuts for 0.5, 1 and 2. outline and filled are true | false,
              // outlineColor is "#RRGGBB" (left out: the accent colour).
              // "overrides": one item's own "width", "outline", "filled" and "outlineColor" (a picker's: "span", "outline", "filled", "outlineColor").
              // "buttons": up to {{MaxButtons}} custom buttons; id a-z, 0-9 and -, label up to {{MaxLabel}} characters, icon a Segoe
              // Fluent Icons code (E000-F8FF), and exactly one action: "quickAction" (the name of a file in actions\) or
              // "text" (put in the message box; "send": true also sends it). "iconFile": "<id>.png" shows that picture from
              // icons\ instead of the icon (PNG, at most 256 KB, 16-256 px a side; Settings copies it there).
              // "menu": ids of buttons (built-in or custom, no pickers) listed in the "more" item's menu instead of on the bar. A row with a
              // "more" item sends the buttons that don't fit into that menu too (after these), and then has no separate "…" button.
              // Needs "more" placed in one of the groups; it is ignored otherwise.
              "version": {{CurrentVersion}},
              "top": {{Row(Quadrant.TopLeft, Quadrant.TopCenter, Quadrant.TopRight)}},
              "items": {{Row(Quadrant.BottomLeft, Quadrant.BottomCenter, Quadrant.BottomRight)}},
              "menu": [{{string.Join(", ", Menu.Select(Str))}}],
              "style": {{StyleText(Style)}},
              "overrides": {{Entries(OverrideTexts(), text => text)}},
              "buttons": {{Entries(Buttons.Select(b => (b.Id, b)), ButtonText)}}
            }
            """;
    }
}
