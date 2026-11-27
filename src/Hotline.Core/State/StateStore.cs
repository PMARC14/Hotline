using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Files;
using Hotline.Core.Settings;

namespace Hotline.Core.State;

/// <summary>
/// Thread-safe reader/writer for <c>state.json</c> (v1): shown flags, saved panel spot, MCP tool grants,
/// <c>lastWritten</c> file hashes and the upgrade record. Preserves unknown keys at every level, writes durably
/// via <see cref="DurableFile"/> without backups, never writes on load, and never overwrites a newer-version file.
/// </summary>
public sealed class StateStore(string filePath, TimeProvider? time = null, IFileSystem? fileSystem = null) : IGrantStore
{
    public const int CurrentVersion = 1;
    public const string FileName = "state.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly IFileSystem _files = fileSystem ?? new RealFileSystem();
    // One lock per file, shared by every instance: import, reset and the App each hold their own store.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lock> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = Gates.GetOrAdd(Path.GetFullPath(filePath), _ => new Lock());
    private JsonObject _root = CreateEmptyTree();
    private byte[]? _lastDiskBytes;
    private int? _newerVersionFile;
    private bool _loaded;
    private bool _dirtyInMemory;
    private bool _corruptOnDisk;

    public string FilePath { get; } = Path.GetFullPath(filePath);

    /// <summary>Version found on disk when it exceeds <see cref="CurrentVersion"/>; otherwise <c>null</c>.</summary>
    public int? NewerVersionFile
    {
        get
        {
            lock (_gate)
            {
                SyncFromDiskLocked();
                return _newerVersionFile;
            }
        }
    }

    /// <summary>Computes the SHA-256 launch hash for an MCP server's command, args and working directory.</summary>
    public static string ComputeLaunchHash(string command, IEnumerable<string>? args = null, string? workingDirectory = null)
    {
        var payload = new object[] { command ?? "", args?.ToArray() ?? [], workingDirectory ?? "" };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    // ---- Shown flags ---------------------------------------------------------------------------

    public ShownState GetShown()
    {
        lock (_gate)
        {
            SyncFromDiskLocked();
            if (GetObject(_root, "shown") is not { } shown) return new ShownState();
            return new ShownState(
                ReadTimestamp(shown, FlagKey(ShownFlag.FirstRun)),
                ReadTimestamp(shown, FlagKey(ShownFlag.SupportNote)),
                ReadTimestamp(shown, FlagKey(ShownFlag.UpgradeNotice)));
        }
    }

    public DateTimeOffset? GetShown(ShownFlag flag)
    {
        lock (_gate)
        {
            SyncFromDiskLocked();
            return GetObject(_root, "shown") is { } shown ? ReadTimestamp(shown, FlagKey(flag)) : null;
        }
    }

    public bool IsShown(ShownFlag flag) => GetShown(flag) is not null;

    public bool MarkShown(ShownFlag flag, DateTimeOffset? at = null)
    {
        lock (_gate)
        {
            if (!PrepareWriteLocked()) return false;
            var key = FlagKey(flag);
            if (GetObject(_root, "shown") is { } existing && ReadTimestamp(existing, key) is not null)
                return !_dirtyInMemory || SaveLocked();

            var shown = EnsureObject(_root, "shown");
            SetProperty(shown, key, JsonValue.Create(FormatTimestamp(at ?? _time.GetUtcNow())));
            return SaveLocked();
        }
    }

    public bool SetShown(ShownFlag flag, DateTimeOffset? at)
    {
        lock (_gate)
        {
            if (!PrepareWriteLocked()) return false;
            var key = FlagKey(flag);
            var existingShown = GetObject(_root, "shown");
            if (at is { } ts)
            {
                if (existingShown is not null && ReadTimestamp(existingShown, key) == ts)
                    return !_dirtyInMemory || SaveLocked();

                var shown = existingShown ?? EnsureObject(_root, "shown");
                SetProperty(shown, key, JsonValue.Create(FormatTimestamp(ts)));
                return SaveLocked();
            }

            if (existingShown is null || FindKey(existingShown, key) is not { } existingKey || existingShown[existingKey] is null)
                return !_dirtyInMemory || SaveLocked();

            existingShown[existingKey] = null;
            return SaveLocked();
        }
    }

    // ---- Saved panel spot ----------------------------------------------------------------------

    public PanelSpot GetPanelSpot()
    {
        lock (_gate)
        {
            SyncFromDiskLocked();
            return ReadPanelSpotLocked();
        }
    }

    public bool SavePanelSpot(
        int widthPx,
        WidthOver? overWidth,
        int? chatHeight,
        int? overChatHeight,
        PanelAnchor anchor,
        PanelAnchor? overAnchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        return SavePanelSpot(new PanelSpot(
            new PanelOverride<int, WidthOver?>(widthPx, overWidth),
            new PanelOverride<int?, int?>(chatHeight, overChatHeight),
            new PanelOverride<PanelAnchor, PanelAnchor?>(anchor, overAnchor)));
    }

    public bool SavePanelSpot(PanelSpot spot)
    {
        ArgumentNullException.ThrowIfNull(spot);
        if (spot.Anchor is { } anchorCheck) ArgumentNullException.ThrowIfNull(anchorCheck.Value);

        lock (_gate)
        {
            if (!PrepareWriteLocked()) return false;
            if (PanelSpotsEqual(ReadPanelSpotLocked(), spot))
                return !_dirtyInMemory || SaveLocked();

            if (spot.Width is null && spot.ChatHeight is null && spot.Anchor is null)
            {
                ClearPanelLocked();
                return SaveLocked();
            }

            var panel = EnsureObject(_root, "panel");
            if (spot.Width is { } w)
            {
                var widthObj = EnsureObject(panel, "width");
                SetProperty(widthObj, "value", JsonValue.Create(w.Value));
                SetProperty(widthObj, "over", ToWidthOverNode(w.Over));
            }
            else
            {
                ClearObjectKeys(panel, "width", "value", "over");
            }

            if (spot.ChatHeight is { } ch)
            {
                var chatObj = EnsureObject(panel, "chatHeight");
                SetProperty(chatObj, "value", ch.Value is { } v ? JsonValue.Create(v) : null);
                SetProperty(chatObj, "over", ch.Over is { } o ? JsonValue.Create(o) : null);
            }
            else
            {
                ClearObjectKeys(panel, "chatHeight", "value", "over");
            }

            if (spot.Anchor is { } a)
            {
                var anchorObj = EnsureObject(panel, "anchor");
                WriteAnchorProperty(anchorObj, "value", a.Value);
                if (a.Over is { } overAnchor) WriteAnchorProperty(anchorObj, "over", overAnchor);
                else SetProperty(anchorObj, "over", null);
            }
            else
            {
                ClearObjectKeys(panel, "anchor", "value", "over");
            }

            if (panel.Count == 0) RemoveKnownKeys(_root, "panel");

            return SaveLocked();
        }
    }

    public bool ClearPanelSpot() => SavePanelSpot(new PanelSpot());

    public ResolvedPanelSpot ResolvePanelSpot(WidthOver? currentWidth, int? currentChatHeight, PanelAnchor currentAnchor)
    {
        ArgumentNullException.ThrowIfNull(currentAnchor);
        PanelSpot saved;
        lock (_gate)
        {
            SyncFromDiskLocked();
            saved = ReadPanelSpotLocked();
        }

        var widthApplied = saved.Width is { } w && Equals(currentWidth, w.Over);
        var chatApplied = saved.ChatHeight is { } ch && currentChatHeight == ch.Over;
        var anchorApplied = saved.Anchor is { } a && AnchorsEqual(currentAnchor, a.Over);

        return new ResolvedPanelSpot(
            widthApplied ? saved.Width!.Value : null,
            chatApplied ? saved.ChatHeight!.Value : currentChatHeight,
            anchorApplied ? CloneAnchor(saved.Anchor!.Value) : CloneAnchor(currentAnchor),
            widthApplied,
            chatApplied,
            anchorApplied);
    }

    // ---- Tool grants ---------------------------------------------------------------------------

    public IReadOnlyList<ToolGrant> GetGrants()
    {
        lock (_gate)
        {
            SyncFromDiskLocked();
            if (GetArray(_root, "grants") is not { } arr) return [];
            var result = new List<ToolGrant>();
            foreach (var item in arr)
                if (ReadGrant(item) is { } grant)
                    result.Add(grant);
            return result;
        }
    }

    public bool IsGranted(string server, string tool, string launchHash)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(tool) || string.IsNullOrWhiteSpace(launchHash))
            return false;
        lock (_gate)
        {
            SyncFromDiskLocked();
            if (GetArray(_root, "grants") is not { } arr) return false;
            foreach (var item in arr)
            {
                if (ReadGrant(item) is { } grant && GrantMatches(grant, server, tool, launchHash))
                    return true;
            }
            return false;
        }
    }

    public bool AddGrant(string server, string tool, string launchHash, DateTimeOffset? at = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchHash);

        lock (_gate)
        {
            if (!PrepareWriteLocked()) return false;
            if (GetArray(_root, "grants") is { } existingArr)
            {
                foreach (var item in existingArr)
                {
                    if (ReadGrant(item) is { } existing && GrantMatches(existing, server, tool, launchHash))
                        return !_dirtyInMemory || SaveLocked();
                }
            }

            var arr = EnsureArray(_root, "grants");
            arr.Add(new JsonObject
            {
                ["server"] = server,
                ["tool"] = tool,
                ["launch"] = launchHash,
                ["at"] = FormatTimestamp(at ?? _time.GetUtcNow()),
            });
            return SaveLocked();
        }
    }

    public bool RevokeGrant(string server, string tool, string? launchHash = null)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(tool)) return false;

        lock (_gate)
        {
            if (!PrepareWriteLocked()) return false;
            if (GetArray(_root, "grants") is not { } arr) return false;

            var removed = false;
            for (var i = arr.Count - 1; i >= 0; i--)
            {
                if (ReadGrant(arr[i]) is { } grant && GrantMatches(grant, server, tool, launchHash))
                {
                    arr.RemoveAt(i);
                    removed = true;
                }
            }

            return removed && SaveLocked();
        }
    }

    public bool RevokeServerGrants(string server)
    {
        if (string.IsNullOrWhiteSpace(server)) return false;

        lock (_gate)
        {
            if (!PrepareWriteLocked()) return false;
            if (GetArray(_root, "grants") is not { } arr) return false;

            var removed = false;
            for (var i = arr.Count - 1; i >= 0; i--)
            {
                if (ReadGrant(arr[i]) is { } grant && string.Equals(grant.Server, server, StringComparison.Ordinal))
                {
                    arr.RemoveAt(i);
                    removed = true;
                }
            }

            return removed && SaveLocked();
        }
    }

    // ---- LastWritten ---------------------------------------------------------------------------

    public string? GetLastWritten(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var key = NormalizeRelativePath(relativePath);
        lock (_gate)
        {
            SyncFromDiskLocked();
            if (GetObject(_root, "lastWritten") is not { } map) return null;
            var actualKey = FindKey(map, key);
            return actualKey is not null ? ReadString(map[actualKey]) : null;
        }
    }

    public IReadOnlyDictionary<string, string> GetAllLastWritten()
    {
        lock (_gate)
        {
            SyncFromDiskLocked();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (GetObject(_root, "lastWritten") is { } map)
            {
                foreach (var (k, v) in map)
                    if (ReadString(v) is { Length: > 0 } hash)
                        result[NormalizeRelativePath(k)] = hash;
            }
            return new ReadOnlyDictionary<string, string>(result);
        }
    }

    public bool SetLastWritten(string relativePath, string? sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var key = NormalizeRelativePath(relativePath);

        lock (_gate)
        {
            if (!PrepareWriteLocked()) return false;
            if (string.IsNullOrWhiteSpace(sha256))
            {
                if (GetObject(_root, "lastWritten") is not { } existing) return !_dirtyInMemory || SaveLocked();
                var actual = FindKey(existing, key);
                if (actual is null) return !_dirtyInMemory || SaveLocked();
                existing.Remove(actual);
                return SaveLocked();
            }

            if (GetObject(_root, "lastWritten") is { } existingMap
                && FindKey(existingMap, key) is { } foundKey
                && string.Equals(ReadString(existingMap[foundKey]), sha256, StringComparison.Ordinal))
            {
                return !_dirtyInMemory || SaveLocked();
            }

            var map = EnsureObject(_root, "lastWritten");
            var existingKey = FindKey(map, key) ?? key;
            map[existingKey] = JsonValue.Create(sha256);
            return SaveLocked();
        }
    }

    // ---- Upgrade record ------------------------------------------------------------------------

    public UpgradeRecord? GetUpgrade()
    {
        lock (_gate)
        {
            SyncFromDiskLocked();
            return ReadUpgradeLocked();
        }
    }

    public bool RecordUpgrade(string? backup, DateTimeOffset? at = null) =>
        SetUpgrade(new UpgradeRecord(at ?? _time.GetUtcNow(), backup));

    public bool SetUpgrade(UpgradeRecord? upgrade)
    {
        lock (_gate)
        {
            if (!PrepareWriteLocked()) return false;
            if (upgrade is null)
            {
                if (!ClearObjectKeys(_root, "upgrade", "at", "backup"))
                    return !_dirtyInMemory || SaveLocked();
                return SaveLocked();
            }

            if (ReadUpgradeLocked() is { } current && current == upgrade)
                return !_dirtyInMemory || SaveLocked();

            var obj = EnsureObject(_root, "upgrade");
            SetProperty(obj, "at", JsonValue.Create(FormatTimestamp(upgrade.At)));
            SetProperty(obj, "backup", upgrade.Backup is null ? null : JsonValue.Create(upgrade.Backup));
            return SaveLocked();
        }
    }

    // ---- Clear ---------------------------------------------------------------------------------

    public bool Clear()
    {
        lock (_gate)
        {
            if (!PrepareWriteLocked()) return false;
            var fileExists = _files.FileExists(FilePath);

            var changed = ClearObjectKeys(_root, "shown", "firstRun", "supportNote", "upgradeNotice");
            changed |= ClearPanelLocked();
            changed |= RemoveKnownKeys(_root, "grants", "lastWritten");
            changed |= ClearObjectKeys(_root, "upgrade", "at", "backup");

            if (!fileExists)
            {
                _dirtyInMemory = false;
                return true;
            }

            if (!changed && !_dirtyInMemory && !_corruptOnDisk)
                return true;

            return SaveLocked();
        }
    }

    // ---- Disk sync & serialization -------------------------------------------------------------

    private bool PrepareWriteLocked()
    {
        SyncFromDiskLocked(readingForWrite: true, out var diskBlocked);
        return !diskBlocked && _newerVersionFile is null;
    }

    private void SyncFromDiskLocked() => SyncFromDiskLocked(readingForWrite: false, out _);

    private void SyncFromDiskLocked(bool readingForWrite, out bool diskBlocked)
    {
        diskBlocked = false;
        if (!_files.FileExists(FilePath))
        {
            if (!_loaded || (_lastDiskBytes is not null && !_dirtyInMemory))
            {
                _root = CreateEmptyTree();
                _newerVersionFile = null;
                _lastDiskBytes = null;
                _loaded = true;
                _dirtyInMemory = false;
                _corruptOnDisk = false;
            }
            return;
        }

        byte[] bytes;
        try
        {
            bytes = _files.ReadAllBytes(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cannot verify version on disk when locked/denied: treat as empty on read and refuse write.
            if (!_loaded)
            {
                _root = CreateEmptyTree();
                _newerVersionFile = null;
                _loaded = true;
            }
            if (readingForWrite) diskBlocked = true;
            return;
        }

        if (_loaded && _lastDiskBytes is not null && bytes.AsSpan().SequenceEqual(_lastDiskBytes))
            return;

        _lastDiskBytes = bytes;
        _loaded = true;
        _dirtyInMemory = false;

        if (JsonFile.TryParseObject(bytes, out var obj))
        {
            _corruptOnDisk = false;
            if (JsonFile.ReadVersion(obj) is { } ver && ver > CurrentVersion)
            {
                _newerVersionFile = ver; // read as far as understood; SaveLocked never writes it
                _root = obj;
                return;
            }

            _newerVersionFile = null;
            _root = obj;
        }
        else
        {
            _corruptOnDisk = true;
            _newerVersionFile = null;
            _root = CreateEmptyTree();
        }
    }

    private bool SaveLocked()
    {
        if (_newerVersionFile is not null) return false;
        EnsureVersionLocked();

        var bytes = JsonFile.ToBytes(_root, WriteOptions);
        if (_lastDiskBytes is not null && bytes.AsSpan().SequenceEqual(_lastDiskBytes) && _files.FileExists(FilePath))
            return true;

        var saved = JsonFile.Save(_files, FilePath, bytes, CurrentVersion);
        if (saved.Result == SafeFileResult.NewerVersion)
        {
            _newerVersionFile = saved.DiskVersion;
            return false;
        }
        if (!saved.Ok)
        {
            _dirtyInMemory = true;
            return false;
        }
        _lastDiskBytes = bytes;
        _loaded = true;
        _dirtyInMemory = false;
        _corruptOnDisk = false;
        return true;
    }

    private void EnsureVersionLocked()
    {
        var key = FindKey(_root, "version");
        if (key is null) _root["version"] = CurrentVersion;
        else if (_root[key] is not JsonValue v || !v.TryGetValue<int>(out var ver) || ver != CurrentVersion)
            _root[key] = CurrentVersion;
    }

    private bool ClearPanelLocked()
    {
        if (GetObject(_root, "panel") is not { } panel)
            return RemoveKnownKeys(_root, "panel");

        var changed = ClearObjectKeys(panel, "width", "value", "over");
        changed |= ClearObjectKeys(panel, "chatHeight", "value", "over");
        changed |= ClearObjectKeys(panel, "anchor", "value", "over");

        if (panel.Count == 0) changed |= RemoveKnownKeys(_root, "panel");
        return changed;
    }

    private static bool ClearObjectKeys(JsonObject parent, string objectKey, params string[] keys)
    {
        if (GetObject(parent, objectKey) is { } obj)
        {
            var removed = RemoveKnownKeys(obj, keys);
            if (obj.Count == 0) removed |= RemoveKnownKeys(parent, objectKey);
            return removed;
        }
        return RemoveKnownKeys(parent, objectKey);
    }

    private PanelSpot ReadPanelSpotLocked()
    {
        if (GetObject(_root, "panel") is not { } panel) return new PanelSpot();

        PanelOverride<int, WidthOver?>? width = null;
        if (GetObject(panel, "width") is { } widthObj
            && ReadInt(GetNode(widthObj, "value")) is { } widthVal
            && TryReadWidthOver(GetNode(widthObj, "over"), out var widthOver))
        {
            width = new PanelOverride<int, WidthOver?>(widthVal, widthOver);
        }

        PanelOverride<int?, int?>? chatHeight = null;
        if (GetObject(panel, "chatHeight") is { } chatObj
            && FindKey(chatObj, "value") is { } chatValueKey
            && TryReadNullableInt(chatObj[chatValueKey], out var chatVal)
            && TryReadNullableInt(GetNode(chatObj, "over"), out var chatOver))
        {
            chatHeight = new PanelOverride<int?, int?>(chatVal, chatOver);
        }

        PanelOverride<PanelAnchor, PanelAnchor?>? anchor = null;
        if (GetObject(panel, "anchor") is { } anchorObj
            && ReadAnchor(GetNode(anchorObj, "value")) is { } anchorVal
            && TryReadNullableAnchor(GetNode(anchorObj, "over"), out var anchorOver))
        {
            anchor = new PanelOverride<PanelAnchor, PanelAnchor?>(anchorVal, anchorOver);
        }

        return new PanelSpot(width, chatHeight, anchor);
    }

    // ---- JSON helpers --------------------------------------------------------------------------

    private static JsonObject CreateEmptyTree() => new() { ["version"] = CurrentVersion };

    private static string FlagKey(ShownFlag flag) => flag switch
    {
        ShownFlag.FirstRun => "firstRun",
        ShownFlag.SupportNote => "supportNote",
        ShownFlag.UpgradeNotice => "upgradeNotice",
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, null),
    };

    private static string FormatTimestamp(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string NormalizeRelativePath(string path) =>
        path.Trim().Replace('\\', '/').TrimStart('/');

    private static string? FindKey(JsonObject obj, string name)
    {
        if (obj.ContainsKey(name)) return name;
        foreach (var (k, _) in obj)
            if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
                return k;
        return null;
    }

    private static JsonNode? GetNode(JsonObject obj, string name) =>
        FindKey(obj, name) is { } key ? obj[key] : null;

    private static JsonObject? GetObject(JsonObject obj, string name) =>
        GetNode(obj, name) as JsonObject;

    private static JsonArray? GetArray(JsonObject obj, string name) =>
        GetNode(obj, name) as JsonArray;

    private static JsonObject EnsureObject(JsonObject parent, string name)
    {
        if (FindKey(parent, name) is { } existing)
        {
            if (parent[existing] is JsonObject o) return o;
            var replacement = new JsonObject();
            parent[existing] = replacement;
            return replacement;
        }
        var created = new JsonObject();
        parent[name] = created;
        return created;
    }

    private static JsonArray EnsureArray(JsonObject parent, string name)
    {
        if (FindKey(parent, name) is { } existing)
        {
            if (parent[existing] is JsonArray a) return a;
            var replacement = new JsonArray();
            parent[existing] = replacement;
            return replacement;
        }
        var created = new JsonArray();
        parent[name] = created;
        return created;
    }

    private static void SetProperty(JsonObject obj, string name, JsonNode? value)
    {
        var key = FindKey(obj, name) ?? name;
        obj[key] = value;
    }

    private static bool RemoveKnownKeys(JsonObject obj, params string[] names)
    {
        var removed = false;
        foreach (var name in names)
            if (FindKey(obj, name) is { } key && obj.Remove(key))
                removed = true;
        return removed;
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static DateTimeOffset? ReadTimestamp(JsonObject obj, string name)
    {
        var text = ReadString(GetNode(obj, name));
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : null;
    }

    private static int? ReadInt(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d) && double.IsInteger(d) && d is >= int.MinValue and <= int.MaxValue)
            return (int)d;
        return null;
    }

    private static bool TryReadNullableInt(JsonNode? node, out int? value)
    {
        if (node is null) { value = null; return true; }
        if (ReadInt(node) is { } i) { value = i; return true; }
        value = null;
        return false;
    }

    private static bool TryReadWidthOver(JsonNode? node, out WidthOver? value)
    {
        if (node is null) { value = null; return true; }
        if (ReadInt(node) is { } px) { value = WidthOver.FromPixels(px); return true; }
        if (ReadString(node) is { } s && !string.IsNullOrWhiteSpace(s)) { value = WidthOver.FromPercent(s); return true; }
        value = null;
        return false;
    }

    private static JsonNode? ToWidthOverNode(WidthOver? over) => over switch
    {
        null => null,
        { Pixels: { } px } => JsonValue.Create(px),
        { Percent: { } pct } => JsonValue.Create(pct),
        _ => null,
    };

    private static double? ReadFiniteDouble(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d) && double.IsFinite(d)) return d;
        return null;
    }

    private static PanelAnchor? ReadAnchor(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;
        var monitor = ReadString(GetNode(obj, "monitor"));
        if (string.IsNullOrWhiteSpace(monitor)) return null;
        if (ReadFiniteDouble(GetNode(obj, "x")) is not { } x) return null;
        if (ReadFiniteDouble(GetNode(obj, "y")) is not { } y) return null;
        return new PanelAnchor { Monitor = monitor, X = x, Y = y };
    }

    private static bool TryReadNullableAnchor(JsonNode? node, out PanelAnchor? value)
    {
        if (node is null) { value = null; return true; }
        if (ReadAnchor(node) is { } anchor) { value = anchor; return true; }
        value = null;
        return false;
    }

    private static void WriteAnchorProperty(JsonObject parent, string key, PanelAnchor anchor)
    {
        var obj = EnsureObject(parent, key);
        SetProperty(obj, "monitor", JsonValue.Create(string.IsNullOrWhiteSpace(anchor.Monitor) ? PanelAnchor.Active : anchor.Monitor));
        SetProperty(obj, "x", JsonValue.Create(anchor.X));
        SetProperty(obj, "y", JsonValue.Create(anchor.Y));
    }

    private static PanelAnchor CloneAnchor(PanelAnchor anchor) => new()
    {
        Monitor = string.IsNullOrWhiteSpace(anchor.Monitor) ? PanelAnchor.Active : anchor.Monitor,
        X = anchor.X,
        Y = anchor.Y,
    };

    private static bool AnchorsEqual(PanelAnchor current, PanelAnchor? over) =>
        over is not null
        && string.Equals(current.Monitor, over.Monitor, StringComparison.OrdinalIgnoreCase)
        && Math.Abs(current.X - over.X) <= 1e-9
        && Math.Abs(current.Y - over.Y) <= 1e-9;

    private UpgradeRecord? ReadUpgradeLocked()
    {
        if (GetObject(_root, "upgrade") is not { } obj) return null;
        if (ReadTimestamp(obj, "at") is not { } at) return null;
        var backupNode = GetNode(obj, "backup");
        string? backup = null;
        if (backupNode is not null)
        {
            if (ReadString(backupNode) is not { } s) return null;
            backup = s;
        }
        return new UpgradeRecord(at, backup);
    }

    private static bool PanelSpotsEqual(PanelSpot a, PanelSpot b) =>
        Equals(a.Width, b.Width)
        && Equals(a.ChatHeight, b.ChatHeight)
        && AnchorOverridesEqual(a.Anchor, b.Anchor);

    private static bool AnchorOverridesEqual(
        PanelOverride<PanelAnchor, PanelAnchor?>? a,
        PanelOverride<PanelAnchor, PanelAnchor?>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return AnchorsEqualExact(a.Value, b.Value) && AnchorsEqualExact(a.Over, b.Over);
    }

    private static bool AnchorsEqualExact(PanelAnchor? a, PanelAnchor? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        var monA = string.IsNullOrWhiteSpace(a.Monitor) ? PanelAnchor.Active : a.Monitor;
        var monB = string.IsNullOrWhiteSpace(b.Monitor) ? PanelAnchor.Active : b.Monitor;
        return string.Equals(monA, monB, StringComparison.Ordinal)
            && a.X == b.X
            && a.Y == b.Y;
    }

    private static ToolGrant? ReadGrant(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;
        var server = ReadString(GetNode(obj, "server"));
        var tool = ReadString(GetNode(obj, "tool"));
        var launch = ReadString(GetNode(obj, "launch"));
        var at = ReadTimestamp(obj, "at");
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(tool)
            || string.IsNullOrWhiteSpace(launch) || at is null)
            return null;
        return new ToolGrant(server, tool, launch, at.Value);
    }

    private static bool GrantMatches(ToolGrant grant, string server, string tool, string? launchHash) =>
        string.Equals(grant.Server, server, StringComparison.Ordinal)
        && string.Equals(grant.Tool, tool, StringComparison.Ordinal)
        && (launchHash is null || string.Equals(grant.Launch, launchHash, StringComparison.OrdinalIgnoreCase));
}
