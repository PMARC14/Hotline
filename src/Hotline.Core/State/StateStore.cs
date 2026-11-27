using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Files;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.State;

/// <summary>
/// Thread-safe reader/writer for <c>state.json</c> (v1): shown flags, saved panel spot, MCP tool grants,
/// <c>lastWritten</c> file hashes and the upgrade record. The sections live in <see cref="StateDocument"/>, merged onto the
/// on-disk tree so unknown keys survive. Writes are durable and never backed up; loading never writes, and a newer-version
/// file is read as far as understood and never overwritten. Share one instance per file: the lock is per instance.
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
    private readonly Lock _gate = new();
    private JsonObject _tree = EmptyTree();
    private StateDocument _doc = StateDocument.Empty;
    private byte[]? _lastDiskBytes;
    private int? _newerVersionFile;
    private bool _loaded;
    private bool _dirtyInMemory; // a write failed: memory is ahead of disk until the file changes or a write succeeds
    private bool _corruptOnDisk;

    public string FilePath { get; } = Path.GetFullPath(filePath);

    /// <summary>Version found on disk when it exceeds <see cref="CurrentVersion"/>; otherwise <c>null</c>.</summary>
    public int? NewerVersionFile => Read(_ => _newerVersionFile);

    /// <summary>Computes the SHA-256 launch hash for an MCP server's command, args and working directory.</summary>
    public static string ComputeLaunchHash(string command, IEnumerable<string>? args = null, string? workingDirectory = null)
    {
        var payload = new object[] { command ?? "", args?.ToArray() ?? [], workingDirectory ?? "" };
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
    }

    // ---- Shown flags ---------------------------------------------------------------------------

    public ShownState GetShown() => Read(d => new ShownState(
        d.Shown.GetValueOrDefault(ShownFlag.FirstRun), d.Shown.GetValueOrDefault(ShownFlag.SupportNote), d.Shown.GetValueOrDefault(ShownFlag.UpgradeNotice)));

    public DateTimeOffset? GetShown(ShownFlag flag) => Read(d => d.Shown.GetValueOrDefault(flag));

    public bool IsShown(ShownFlag flag) => GetShown(flag) is not null;

    public bool MarkShown(ShownFlag flag, DateTimeOffset? at = null) =>
        Update(d => d.Shown.GetValueOrDefault(flag) is not null ? null : WithShown(d, flag, at ?? _time.GetUtcNow()));

    public bool SetShown(ShownFlag flag, DateTimeOffset? at) =>
        Update(d => d.Shown.GetValueOrDefault(flag) == at ? null : WithShown(d, flag, at));

    private static StateDocument WithShown(StateDocument d, ShownFlag flag, DateTimeOffset? at) =>
        d with { Shown = new Dictionary<ShownFlag, DateTimeOffset?>(d.Shown) { [flag] = at } };

    // ---- Saved panel spot ----------------------------------------------------------------------

    public PanelSpot GetPanelSpot() => Read(d => d.Panel);

    public bool SavePanelSpot(int widthPx, WidthOver? overWidth, int? chatHeight, int? overChatHeight, PanelAnchor anchor, PanelAnchor? overAnchor)
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
        return Update(d => d with { Panel = spot });
    }

    public bool ClearPanelSpot() => SavePanelSpot(new PanelSpot());

    public ResolvedPanelSpot ResolvePanelSpot(WidthOver? currentWidth, int? currentChatHeight, PanelAnchor currentAnchor)
    {
        ArgumentNullException.ThrowIfNull(currentAnchor);
        return PanelSpotPolicy.ResolveSaved(GetPanelSpot(), currentWidth, currentChatHeight, currentAnchor);
    }

    // ---- Tool grants ---------------------------------------------------------------------------

    public IReadOnlyList<ToolGrant> GetGrants() => Read(d => d.Grants);

    public bool IsGranted(string server, string tool, string launchHash) =>
        !string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(tool) && !string.IsNullOrWhiteSpace(launchHash)
        && Read(d => d.Grants.Any(g => GrantMatches(g, server, tool, launchHash)));

    public bool AddGrant(string server, string tool, string launchHash, DateTimeOffset? at = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchHash);
        return Update(d => d.Grants.Any(g => GrantMatches(g, server, tool, launchHash))
            ? null
            : d with { Grants = [.. d.Grants, new ToolGrant(server, tool, launchHash, at ?? _time.GetUtcNow())] });
    }

    public bool RevokeGrant(string server, string tool, string? launchHash = null) =>
        !string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(tool)
        && Update(d => Without(d, g => GrantMatches(g, server, tool, launchHash)), noChangeIsOk: false);

    public bool RevokeServerGrants(string server) =>
        !string.IsNullOrWhiteSpace(server)
        && Update(d => Without(d, g => string.Equals(g.Server, server, StringComparison.Ordinal)), noChangeIsOk: false);

    private static StateDocument? Without(StateDocument d, Func<ToolGrant, bool> remove) =>
        d.Grants.Any(remove) ? d with { Grants = d.Grants.Where(g => !remove(g)).ToList() } : null;

    private static bool GrantMatches(ToolGrant grant, string server, string tool, string? launchHash) =>
        string.Equals(grant.Server, server, StringComparison.Ordinal)
        && string.Equals(grant.Tool, tool, StringComparison.Ordinal)
        && (launchHash is null || string.Equals(grant.Launch, launchHash, StringComparison.OrdinalIgnoreCase));

    // ---- LastWritten ---------------------------------------------------------------------------

    public string? GetLastWritten(string relativePath) =>
        string.IsNullOrWhiteSpace(relativePath) ? null : Read(d => d.LastWritten.GetValueOrDefault(StateDocument.NormalizePath(relativePath)));

    public IReadOnlyDictionary<string, string> GetAllLastWritten() =>
        Read(d => new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(d.LastWritten, StringComparer.OrdinalIgnoreCase)));

    public bool SetLastWritten(string relativePath, string? sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var key = StateDocument.NormalizePath(relativePath);
        return Update(d =>
        {
            var map = new Dictionary<string, string>(d.LastWritten, StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(sha256)) map.Remove(key);
            else map[key] = sha256;
            return d with { LastWritten = map };
        });
    }

    // ---- Upgrade record ------------------------------------------------------------------------

    public UpgradeRecord? GetUpgrade() => Read(d => d.Upgrade);

    public bool RecordUpgrade(string? backup, DateTimeOffset? at = null) => SetUpgrade(new UpgradeRecord(at ?? _time.GetUtcNow(), backup));

    public bool SetUpgrade(UpgradeRecord? upgrade) => Update(d => d with { Upgrade = upgrade });

    // ---- Clear ---------------------------------------------------------------------------------

    /// <summary>Removes every known section (unknown keys stay). Creates no file when none exists.</summary>
    public bool Clear()
    {
        lock (_gate)
        {
            if (!PrepareWrite()) return false;
            var exists = _files.FileExists(FilePath);
            var edited = Edit(StateDocument.Empty);
            var changed = !JsonNode.DeepEquals(edited, _tree);
            _tree = edited;
            _doc = StateDocument.Read(_tree);
            if (!exists)
            {
                _dirtyInMemory = false;
                return true;
            }
            return (!changed && !_dirtyInMemory && !_corruptOnDisk) || Save();
        }
    }

    // ---- Locking, disk sync and saving ---------------------------------------------------------

    private T Read<T>(Func<StateDocument, T> pick)
    {
        lock (_gate)
        {
            Sync(forWrite: false, out _);
            return pick(_doc);
        }
    }

    /// <summary>Applies <paramref name="change"/> (null = nothing to change) and saves. An unchanged state succeeds without
    /// rewriting the file, unless an earlier write failed and still needs retrying.</summary>
    private bool Update(Func<StateDocument, StateDocument?> change, bool noChangeIsOk = true)
    {
        lock (_gate)
        {
            if (!PrepareWrite()) return false;
            var edited = change(_doc) is { } next ? Edit(next) : null;
            if (edited is null || JsonNode.DeepEquals(edited, _tree))
                return noChangeIsOk && (!_dirtyInMemory || Save());
            _tree = edited;
            _doc = StateDocument.Read(_tree);
            return Save();
        }
    }

    private JsonObject Edit(StateDocument next)
    {
        var edited = (JsonObject)_tree.DeepClone();
        StateDocument.Apply(edited, _doc, next);
        return edited;
    }

    private bool PrepareWrite()
    {
        Sync(forWrite: true, out var blocked);
        return !blocked && _newerVersionFile is null;
    }

    private void Sync(bool forWrite, out bool blocked)
    {
        blocked = false;
        if (!_files.FileExists(FilePath))
        {
            if (!_loaded || (_lastDiskBytes is not null && !_dirtyInMemory)) Reset(null);
            return;
        }

        byte[] bytes;
        try { bytes = _files.ReadAllBytes(FilePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cannot verify the version on disk when locked or denied: read as empty and refuse to write.
            if (!_loaded) Reset(null);
            blocked = forWrite;
            return;
        }

        if (_loaded && _lastDiskBytes is not null && bytes.AsSpan().SequenceEqual(_lastDiskBytes)) return;
        Reset(bytes);
    }

    private void Reset(byte[]? bytes)
    {
        _lastDiskBytes = bytes;
        _loaded = true;
        _dirtyInMemory = false;
        var parsed = bytes is not null && JsonFile.TryParseObject(bytes, out _tree);
        _corruptOnDisk = bytes is not null && !parsed;
        if (!parsed) _tree = EmptyTree();
        _newerVersionFile = parsed && JsonFile.ReadVersion(_tree) is { } version && version > CurrentVersion ? version : null;
        _doc = StateDocument.Read(_tree);
    }

    private bool Save()
    {
        if (_newerVersionFile is not null) return false;
        var key = JsonFile.FindKey(_tree, "version");
        if (key is null) _tree["version"] = CurrentVersion;
        else if (_tree[key] is not JsonValue v || !v.TryGetValue<int>(out var ver) || ver != CurrentVersion) _tree[key] = CurrentVersion;

        var bytes = JsonFile.ToBytes(_tree, WriteOptions);
        if (_lastDiskBytes is not null && bytes.AsSpan().SequenceEqual(_lastDiskBytes) && _files.FileExists(FilePath)) return true;

        var saved = JsonFile.Save(_files, FilePath, bytes, CurrentVersion);
        if (saved.Result == SafeFileResult.NewerVersion)
        {
            _newerVersionFile = saved.DiskVersion;
            return false;
        }
        _dirtyInMemory = !saved.Ok;
        if (!saved.Ok) return false;
        _lastDiskBytes = bytes;
        _corruptOnDisk = false;
        return true;
    }

    private static JsonObject EmptyTree() => new() { ["version"] = CurrentVersion };
}
