using System.Globalization;
using System.Text.Json.Nodes;
using Hotline.Core.Files;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.State;

/// <summary>
/// The sections of <c>state.json</c> (v1) that Hotline owns, read leniently from the on-disk tree and written back by
/// merging the difference onto that tree, so unknown keys survive at every level. A malformed field reads as absent.
/// </summary>
public sealed record StateDocument(
    IReadOnlyDictionary<ShownFlag, DateTimeOffset?> Shown,
    PanelSpot Panel,
    IReadOnlyList<ToolGrant> Grants,
    IReadOnlyDictionary<string, string> LastWritten,
    UpgradeRecord? Upgrade)
{
    public static StateDocument Empty { get; } = new(
        new Dictionary<ShownFlag, DateTimeOffset?>(), new PanelSpot(), [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null);

    // Containers that are dropped once a write empties them (children before parents).
    private static readonly string[][] ContainerPaths =
        [["panel", "width"], ["panel", "chatHeight"], ["panel", "anchor"], ["panel"], ["shown"], ["lastWritten"], ["upgrade"]];

    public static string FlagKey(ShownFlag flag) => flag switch
    {
        ShownFlag.FirstRun => "firstRun",
        ShownFlag.SupportNote => "supportNote",
        ShownFlag.UpgradeNotice => "upgradeNotice",
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, null),
    };

    public static string FormatTimestamp(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static string NormalizePath(string path) => path.Trim().Replace('\\', '/').TrimStart('/');

    // ---- Reading -------------------------------------------------------------------------------

    public static StateDocument Read(JsonObject root)
    {
        var shown = new Dictionary<ShownFlag, DateTimeOffset?>();
        if (Child(root, "shown") is { } shownObj)
            foreach (var flag in Enum.GetValues<ShownFlag>())
                if (JsonFile.FindKey(shownObj, FlagKey(flag)) is { } key) shown[flag] = ReadTimestamp(shownObj[key]);

        var grants = new List<ToolGrant>();
        if (Node(root, "grants") is JsonArray array)
            foreach (var item in array)
                if (ReadGrant(item) is { } grant) grants.Add(grant);

        var lastWritten = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Child(root, "lastWritten") is { } map)
            foreach (var (key, value) in map)
                if (ReadString(value) is { Length: > 0 } hash) lastWritten[NormalizePath(key)] = hash;

        return new StateDocument(shown, ReadPanel(root), grants, lastWritten, ReadUpgrade(root));
    }

    private static PanelSpot ReadPanel(JsonObject root)
    {
        if (Child(root, "panel") is not { } panel) return new PanelSpot();

        PanelOverride<int, WidthOver?>? width = null;
        if (Child(panel, "width") is { } widthObj && ReadInt(Node(widthObj, "value")) is { } widthValue
            && TryReadWidthOver(Node(widthObj, "over"), out var widthOver))
            width = new(widthValue, widthOver);

        PanelOverride<int?, int?>? chat = null;
        if (Child(panel, "chatHeight") is { } chatObj && JsonFile.FindKey(chatObj, "value") is { } chatKey
            && TryReadNullableInt(chatObj[chatKey], out var chatValue) && TryReadNullableInt(Node(chatObj, "over"), out var chatOver))
            chat = new(chatValue, chatOver);

        PanelOverride<PanelAnchor, PanelAnchor?>? anchor = null;
        if (Child(panel, "anchor") is { } anchorObj && ReadAnchor(Node(anchorObj, "value")) is { } anchorValue
            && TryReadNullableAnchor(Node(anchorObj, "over"), out var anchorOver))
            anchor = new(anchorValue, anchorOver);

        return new PanelSpot(width, chat, anchor);
    }

    private static UpgradeRecord? ReadUpgrade(JsonObject root)
    {
        if (Child(root, "upgrade") is not { } obj || ReadTimestamp(Node(obj, "at")) is not { } at) return null;
        var backupNode = Node(obj, "backup");
        if (backupNode is null) return new UpgradeRecord(at, null);
        return ReadString(backupNode) is { } backup ? new UpgradeRecord(at, backup) : null;
    }

    private static ToolGrant? ReadGrant(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;
        var server = ReadString(Node(obj, "server"));
        var tool = ReadString(Node(obj, "tool"));
        var launch = ReadString(Node(obj, "launch"));
        var at = ReadTimestamp(Node(obj, "at"));
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(tool) || string.IsNullOrWhiteSpace(launch) || at is null) return null;
        return new ToolGrant(server, tool, launch, at.Value);
    }

    // ---- Writing -------------------------------------------------------------------------------

    /// <summary>Edits <paramref name="tree"/> in place so it carries <paramref name="after"/> instead of <paramref name="before"/>.
    /// Sections are modelled as objects even when empty, so clearing a section removes only the keys Hotline knows.</summary>
    public static void Apply(JsonObject tree, StateDocument before, StateDocument after)
    {
        var emptyBefore = ContainerPaths.Where(p => Walk(tree, p) is { Count: 0 }).Select(p => string.Join('/', p)).ToHashSet();
        var oldModel = before.ToModel();
        var newModel = after.ToModel();

        if (!JsonNode.DeepEquals(oldModel["panel"], newModel["panel"])) PrefillPanel(tree, after.Panel);
        if (!JsonNode.DeepEquals(oldModel["upgrade"], newModel["upgrade"]) && after.Upgrade is not null)
            Prefill(EnsureObject(tree, "upgrade"), "at", "backup");

        AlignPaths(tree, oldModel);
        AlignPaths(tree, newModel);
        JsonFile.Merge(tree, oldModel, newModel);
        if (!before.Grants.SequenceEqual(after.Grants)) ReconcileGrants(tree, after.Grants);

        foreach (var path in ContainerPaths)
            if (Walk(tree, path) is { Count: 0 } && !emptyBefore.Contains(string.Join('/', path)))
            {
                var parent = Walk(tree, path[..^1])!;
                parent.Remove(JsonFile.FindKey(parent, path[^1])!);
            }
    }

    /// <summary>The model's lastWritten keys take the file's own spelling of the same path ("/Icons/A.png" on disk is read as
    /// "Icons/A.png"), so an update or removal edits that key instead of adding a second one or missing it.</summary>
    private static void AlignPaths(JsonObject tree, JsonObject model)
    {
        if (Child(tree, "lastWritten") is not { } disk || model["lastWritten"] is not JsonObject paths) return;
        foreach (var key in paths.Select(p => p.Key).ToArray())
        {
            var diskKey = disk.Select(p => p.Key).FirstOrDefault(k => string.Equals(NormalizePath(k), key, StringComparison.OrdinalIgnoreCase));
            if (diskKey is null || diskKey == key) continue;
            var value = paths[key];
            paths.Remove(key);
            paths[diskKey] = value?.DeepClone();
        }
    }

    private JsonObject ToModel()
    {
        var shown = new JsonObject();
        foreach (var flag in Enum.GetValues<ShownFlag>())
            if (Shown.TryGetValue(flag, out var at)) shown[FlagKey(flag)] = at is { } t ? FormatTimestamp(t) : null;

        var lastWritten = new JsonObject();
        foreach (var (path, hash) in LastWritten) lastWritten[path] = hash;

        var panel = new JsonObject
        {
            ["width"] = Panel.Width is { } w
                ? new JsonObject { ["value"] = w.Value, ["over"] = w.Over switch { { Pixels: { } px } => px, { Percent: { } pct } => JsonValue.Create(pct), _ => null } }
                : new JsonObject(),
            ["chatHeight"] = Panel.ChatHeight is { } c ? new JsonObject { ["value"] = c.Value, ["over"] = c.Over } : new JsonObject(),
            ["anchor"] = Panel.Anchor is { } a
                ? new JsonObject { ["value"] = AnchorNode(a.Value), ["over"] = a.Over is { } o ? AnchorNode(o) : null }
                : new JsonObject(),
        };

        return new JsonObject
        {
            ["shown"] = shown,
            ["panel"] = panel,
            ["lastWritten"] = lastWritten,
            ["upgrade"] = Upgrade is { } u ? new JsonObject { ["at"] = FormatTimestamp(u.At), ["backup"] = u.Backup } : new JsonObject(),
        };
    }

    private static JsonObject AnchorNode(PanelAnchor anchor) => new()
    {
        ["monitor"] = PanelSpotPolicy.CloneAnchor(anchor).Monitor,
        ["x"] = anchor.X,
        ["y"] = anchor.Y,
    };

    // JsonMerge writes no null into a new object; Hotline's files spell "no value" as an explicit null, so those keys go down first.
    private static void PrefillPanel(JsonObject tree, PanelSpot spot)
    {
        var panel = EnsureObject(tree, "panel");
        if (spot.Width is not null) Prefill(EnsureObject(panel, "width"), "value", "over");
        if (spot.ChatHeight is not null) Prefill(EnsureObject(panel, "chatHeight"), "value", "over");
        if (spot.Anchor is not null) Prefill(EnsureObject(panel, "anchor"), "value", "over");
    }

    private static void Prefill(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
            if (JsonFile.FindKey(obj, key) is null) obj[key] = null;
    }

    /// <summary>Keeps the original JSON of every grant that stays (unknown keys included), drops revoked ones, appends new ones.</summary>
    private static void ReconcileGrants(JsonObject tree, IReadOnlyList<ToolGrant> wanted)
    {
        var array = EnsureArray(tree, "grants");
        var remaining = wanted.ToList();
        for (var i = 0; i < array.Count;)
        {
            if (ReadGrant(array[i]) is { } grant && !remaining.Remove(grant)) array.RemoveAt(i);
            else i++;
        }
        foreach (var grant in remaining)
            array.Add(new JsonObject { ["server"] = grant.Server, ["tool"] = grant.Tool, ["launch"] = grant.Launch, ["at"] = FormatTimestamp(grant.At) });
        if (array.Count == 0) tree.Remove(JsonFile.FindKey(tree, "grants")!);
    }

    // ---- Tree helpers --------------------------------------------------------------------------

    private static JsonNode? Node(JsonObject obj, string name) => JsonFile.FindKey(obj, name) is { } key ? obj[key] : null;

    private static JsonObject? Child(JsonObject obj, string name) => Node(obj, name) as JsonObject;

    private static JsonObject? Walk(JsonObject tree, string[] path)
    {
        var current = tree;
        foreach (var name in path)
        {
            var next = Child(current, name);
            if (next is null) return null;
            current = next;
        }
        return current;
    }

    private static JsonObject EnsureObject(JsonObject parent, string name) => Ensure(parent, name, () => new JsonObject());

    private static JsonArray EnsureArray(JsonObject parent, string name) => Ensure(parent, name, () => new JsonArray());

    private static T Ensure<T>(JsonObject parent, string name, Func<T> create) where T : JsonNode
    {
        var key = JsonFile.FindKey(parent, name) ?? name;
        if (parent[key] is T existing) return existing;
        var created = create();
        parent[key] = created;
        return created;
    }

    private static string? ReadString(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static DateTimeOffset? ReadTimestamp(JsonNode? node) =>
        ReadString(node) is { } text && !string.IsNullOrWhiteSpace(text)
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;

    private static int? ReadInt(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        return v.TryGetValue<double>(out var d) && double.IsInteger(d) && d is >= int.MinValue and <= int.MaxValue ? (int)d : null;
    }

    private static bool TryReadNullableInt(JsonNode? node, out int? value)
    {
        value = null;
        if (node is null) return true;
        if (ReadInt(node) is not { } i) return false;
        value = i;
        return true;
    }

    private static bool TryReadWidthOver(JsonNode? node, out WidthOver? value)
    {
        value = null;
        if (node is null) return true;
        if (ReadInt(node) is { } px) { value = WidthOver.FromPixels(px); return true; }
        if (ReadString(node) is { } s && !string.IsNullOrWhiteSpace(s)) { value = WidthOver.FromPercent(s); return true; }
        return false;
    }

    private static double? ReadFiniteDouble(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : null;

    private static PanelAnchor? ReadAnchor(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;
        var monitor = ReadString(Node(obj, "monitor"));
        if (string.IsNullOrWhiteSpace(monitor) || ReadFiniteDouble(Node(obj, "x")) is not { } x || ReadFiniteDouble(Node(obj, "y")) is not { } y) return null;
        return new PanelAnchor { Monitor = monitor, X = x, Y = y };
    }

    private static bool TryReadNullableAnchor(JsonNode? node, out PanelAnchor? value)
    {
        value = null;
        if (node is null) return true;
        if (ReadAnchor(node) is not { } anchor) return false;
        value = anchor;
        return true;
    }

    /// <summary>
    /// Reset: removes every section this version knows, readable or not, by structure rather than by model (a later 1.x
    /// may read more shapes, so nothing "reset" can come back): the shown flags, the panel items' value/over, all grants
    /// and lastWritten entries, the upgrade record. Unknown keys stay; containers left empty go.
    /// </summary>
    public static void ClearKnown(JsonObject tree)
    {
        ClearKeys(tree, "shown", FlagKey(ShownFlag.FirstRun), FlagKey(ShownFlag.SupportNote), FlagKey(ShownFlag.UpgradeNotice));
        if (JsonFile.FindKey(tree, "panel") is { } panelKey)
        {
            if (tree[panelKey] is JsonObject panel)
            {
                foreach (var item in new[] { "width", "chatHeight", "anchor" }) ClearKeys(panel, item, "value", "over");
                if (panel.Count == 0) tree.Remove(panelKey);
            }
            else tree.Remove(panelKey);
        }
        Remove(tree, "grants");
        Remove(tree, "lastWritten");
        ClearKeys(tree, "upgrade", "at", "backup");
    }

    /// <summary>Removes <paramref name="keys"/> from the object at <paramref name="name"/> (any case), and the object itself
    /// when it is left empty or isn't an object.</summary>
    private static void ClearKeys(JsonObject parent, string name, params string[] keys)
    {
        if (JsonFile.FindKey(parent, name) is not { } key) return;
        if (parent[key] is JsonObject obj)
        {
            foreach (var k in keys) Remove(obj, k);
            if (obj.Count == 0) parent.Remove(key);
        }
        else parent.Remove(key);
    }

    private static void Remove(JsonObject obj, string name)
    {
        while (JsonFile.FindKey(obj, name) is { } key) obj.Remove(key); // every spelling of it
    }
}
