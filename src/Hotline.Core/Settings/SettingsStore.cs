using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Hotline.Core.Files;
using Hotline.Core.State;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Settings;

/// <summary>Read-only loading and lossless, backed-up edits of settings and connection v1 files.</summary>
public sealed class SettingsStore
{
    public const string FileName = "settings.json", ConnectionsFolder = "connections";
    private readonly string _directory;
    private readonly IFileSystem _files;
    private readonly StateStore _state;
    private readonly SafeFile _writer;
    private readonly Dictionary<string, string> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonObject> _before = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _connectionOrder = [];
    private List<string> _mainProblems = [], _connectionProblems = [], _writeProblems = [];
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public SettingsStore(string directory, IFileSystem? fileSystem = null, StateStore? state = null)
    {
        _directory = Path.GetFullPath(directory);
        _files = fileSystem ?? new RealFileSystem();
        _writer = new SafeFile(_files);
        _state = state ?? new StateStore(Path.Combine(_directory, StateStore.FileName)); // tests; the app passes its one instance
    }

    /// <summary>The <c>state.json</c> store this settings store records <c>lastWritten</c> hashes in; reset and import use the same one.</summary>
    public StateStore State => _state;

    public string FilePath => Path.Combine(_directory, FileName);
    public string ConnectionsDirectory => Path.Combine(_directory, ConnectionsFolder);
    public IReadOnlyList<string> BrokenConnectionFiles { get; private set; } = [];
    public int? NewerVersionFile { get; private set; }
    public string? SettingsError { get; private set; }
    public bool SettingsFileDamaged => SettingsError is not null || _mainProblems.Count > 0;
    public IReadOnlyList<string> SettingsProblems => [.. _mainProblems, .. _connectionProblems, .. _writeProblems];
    public IReadOnlyList<string> WriteProblems => _writeProblems;
    public IReadOnlyCollection<string> KnownConnectionIds => [.. _sources.Keys];
    public string? SourcePath(string id) => _sources.GetValueOrDefault(id);
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    internal static readonly JsonSerializerOptions Options = CreateOptions();
    internal static readonly JsonSerializerOptions ConnectionOptions = CreateOptions(omitNull: true);
    private static JsonSerializerOptions CreateOptions(bool omitNull = false) => new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        DefaultIgnoreCondition = omitNull ? JsonIgnoreCondition.WhenWritingNull : JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) },
    };

    public HotlineSettings Load() => Read(keepOnFailure: false)!;
    public HotlineSettings? TryRead() => Read(keepOnFailure: true);

    private HotlineSettings? Read(bool keepOnFailure)
    {
        _mainProblems = [];
        SettingsError = null;
        NewerVersionFile = null;
        HotlineSettings? settings = null;
        try
        {
            if (_files.FileExists(FilePath))
            {
                var problems = new ReadProblems();
                var parsed = ParseSettings(Decode(_files.ReadAllBytes(FilePath)), problems);
                settings = keepOnFailure && parsed.Failure is not null ? null : parsed.Settings;
                _mainProblems = problems.All.ToList();
                SettingsError = parsed.Failure;
            }
            else if (keepOnFailure) SettingsError = L.F(Strings.Settings_ProblemUnreadable, FileName, nameof(FileNotFoundException));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SettingsError = L.F(Strings.Settings_ProblemUnreadable, FileName, ex.GetType().Name); }
        if (settings is null && keepOnFailure) return null;
        settings ??= new HotlineSettings();
        NewerVersionFile = settings.SchemaVersion > HotlineSettings.CurrentSchemaVersion ? settings.SchemaVersion : null;
        settings.Chat.Connections = ReadConnections(settings.Chat.Order);
        Normalize(settings);
        _before[FilePath] = Model(settings, Options);
        foreach (var profile in settings.Chat.Connections)
            _before[_sources[profile.Id]] = Model(profile, ConnectionOptions);
        _connectionOrder = settings.Chat.Connections.Select(p => p.Id).ToList();
        return settings;
    }

    internal static (HotlineSettings? Settings, List<ConnectionProfile>? Legacy, string? Failure) ParseSettings(string text, ReadProblems problems)
    {
        if (!LenientReader.TryParse(text, FileName, out var document, out var failure)) return (null, null, failure);
        using (document)
        {
            var settings = new HotlineSettings();
            var number = ReadVersion(document!.RootElement);
            if (number == 0)
            {
                problems.Add(Strings.Settings_PreFreeze);
                return (settings, null, Strings.Settings_PreFreeze);
            }
            LenientReader.Populate(document.RootElement, settings, FileName, "", problems);
            settings.SchemaVersion = number;
            return (Normalize(settings), null, null);
        }
    }

    internal static ConnectionProfile? ParseConnection(string text, string label, string fileStem, ReadProblems problems, out string? failure)
    {
        failure = null;
        if (!ValidConnectionId(fileStem)) { failure = L.F(Strings.Settings_InvalidConnectionId, LenientReader.Quote(fileStem)); return null; }
        if (!LenientReader.TryParse(text, label, out var document, out failure)) return null;
        using (document)
        {
            var number = ReadVersion(document!.RootElement);
            if (number == 0)
            { failure = Strings.Settings_PreFreeze; return null; }
            var profile = LenientReader.ReadProfile(document.RootElement, label, problems);
            if (profile is null) { failure = problems.All.LastOrDefault() ?? label; return null; }
            profile.Id = fileStem;
            profile.Version = number;
            if (string.IsNullOrWhiteSpace(profile.Name)) profile.Name = fileStem;
            return profile;
        }
    }

    // A ceiling capped at int32 preserves the newer/current/pre-freeze classification in the typed model.
    private static int ReadVersion(JsonElement root) => Files.JsonFile.VersionValue(root) is { } number && number >= 1
        ? (int)Math.Min(int.MaxValue, Math.Ceiling(number)) : 0;

    private List<ConnectionProfile> ReadConnections(List<string> order)
    {
        var profiles = new List<ConnectionProfile>();
        var broken = new List<string>();
        var problems = new ReadProblems();
        try
        {
            if (_files.DirectoryExists(ConnectionsDirectory))
                foreach (var path in _files.EnumerateFiles(ConnectionsDirectory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
                {
                    var label = $"{ConnectionsFolder}/{Path.GetFileName(path)}";
                    try
                    {
                        var profile = ParseConnection(Decode(_files.ReadAllBytes(path)), label, Path.GetFileNameWithoutExtension(path), problems, out var failure);
                        if (profile is null || profiles.Any(p => p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (failure is not null)
                            { if (!problems.All.Contains(failure)) problems.Add(failure); }
                            else problems.Add(L.F(Strings.Settings_ProblemDuplicateId, label, LenientReader.Quote(profile!.Id)));
                            broken.Add(path);
                            continue;
                        }
                        profiles.Add(profile);
                        _sources[profile.Id] = path;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { problems.Add(L.F(Strings.Settings_ProblemUnreadable, label, ex.GetType().Name)); broken.Add(path); }
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(L.F(Strings.Settings_ProblemUnreadable, ConnectionsFolder, ex.GetType().Name));
            broken.AddRange(_sources.Values);
        }
        BrokenConnectionFiles = broken;
        _connectionProblems = problems.All.ToList();
        var rank = order.Select((id, i) => (id, i)).Where(x => !string.IsNullOrWhiteSpace(x.id))
            .GroupBy(x => x.id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
        return profiles.OrderBy(p => rank.GetValueOrDefault(p.Id, int.MaxValue)).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Serialize(HotlineSettings settings) => JsonSerializer.Serialize(settings, Options);
    private static JsonObject Model<T>(T model, JsonSerializerOptions options) => JsonNode.Parse(JsonSerializer.Serialize(model, options))!.AsObject();
    private static string Decode(byte[] bytes) => Encoding.UTF8.GetString(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? bytes.AsSpan(3) : bytes);

    private static JsonNode? RetainedTree(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ObjectTree(element),
        JsonValueKind.Array => new JsonArray(element.EnumerateArray().Select(RetainedTree).ToArray()),
        _ => JsonNode.Parse(element.GetRawText()),
    };

    private static JsonObject ObjectTree(JsonElement element)
    {
        var tree = new JsonObject();
        foreach (var member in element.EnumerateObject()) tree[member.Name] = RetainedTree(member.Value);
        return tree;
    }

    // Match the reader's field spelling without changing case-sensitive map keys such as tool rules.
    private static JsonObject MatchKeys(JsonObject model, JsonObject tree)
    {
        var matched = new JsonObject();
        foreach (var (key, value) in model)
        {
            var actual = tree.LastOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Key ?? key;
            matched[actual] = key != "approvals" && value is JsonObject child && tree[actual] is JsonObject original
                ? MatchKeys(child, original) : value?.DeepClone();
        }
        return matched;
    }

    public void Save(HotlineSettings settings, IEnumerable<string>? removed = null)
    {
        _writeProblems = [];
        var backup = new BackupSet(_directory, BackupReason.Save, _files, Clock);
        var ids = settings.Chat.Connections.Select(p => p.Id).ToList();
        if (!_connectionOrder.SequenceEqual(ids, StringComparer.OrdinalIgnoreCase)) settings.Chat.Order = ids;
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in settings.Chat.Connections)
        {
            if (!ValidConnectionId(profile.Id))
            { _writeProblems.Add(L.F(Strings.Settings_InvalidConnectionId, LenientReader.Quote(profile.Id))); continue; }
            var path = _sources.GetValueOrDefault(profile.Id) ?? ConnectionPath(profile.Id);
            if (!claimed.Add(path))
            { _writeProblems.Add(L.F(Strings.Settings_ProblemDuplicateId, Path.GetFileName(path), LenientReader.Quote(profile.Id))); continue; }
            if (BrokenConnectionFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            { _writeProblems.Add(L.F(Strings.Settings_ProblemWriteFailed, Path.GetFileName(path), nameof(InvalidDataException))); continue; }
            if (SaveFile(path, Model(profile, ConnectionOptions), backup, ConnectionOptions)) _sources[profile.Id] = path;
        }
        var alive = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in (removed ?? _sources.Keys.ToArray()).Where(id => !alive.Contains(id)).ToArray())
        {
            if (!_sources.TryGetValue(id, out var path)) continue;
            try
            {
                if (BrokenConnectionFiles.Contains(path, StringComparer.OrdinalIgnoreCase) || DiskVersion(path) > 1)
                { _writeProblems.Add(L.F(Strings.Settings_ProblemDeleteFailed, Path.GetFileName(path))); continue; }
                if (_files.FileExists(path) && !backup.TryBackup(path))
                { _writeProblems.Add(L.F(Strings.Settings_ProblemBackupFailed, Path.GetFileName(path), nameof(IOException))); continue; }
                _files.DeleteFile(path);
                _sources.Remove(id);
                _before.Remove(path);
                _state.SetLastWritten(Path.GetRelativePath(_directory, path), null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { _writeProblems.Add(L.F(Strings.Settings_ProblemDeleteFailed, Path.GetFileName(path))); }
        }
        if (_writeProblems.Count == 0 && SaveFile(FilePath, Model(settings, Options), backup, Options))
        { SettingsError = null; _connectionOrder = ids; }
    }

    private int DiskVersion(string path)
    {
        if (!_files.FileExists(path)) return 1;
        using var document = JsonDocument.Parse(Decode(_files.ReadAllBytes(path)), ReadOptions);
        return ReadVersion(document.RootElement);
    }

    private bool SaveFile(string path, JsonObject after, BackupSet backup, JsonSerializerOptions options)
    {
        try
        {
            byte[]? original = _files.FileExists(path) ? _files.ReadAllBytes(path) : null;
            JsonObject? tree = null;
            if (original is not null)
            {
                try
                {
                    using var document = JsonDocument.Parse(Decode(original), ReadOptions);
                    tree = RetainedTree(document.RootElement) as JsonObject;
                }
                catch (JsonException) { }
            }
            var before = _before.GetValueOrDefault(path);
            if (before is null && tree is not null)
            {
                var problems = new ReadProblems();
                if (path == FilePath) before = Model(ParseSettings(Decode(original!), problems).Settings ?? new HotlineSettings(), options);
                else if (ParseConnection(Decode(original!), path, Path.GetFileNameWithoutExtension(path), problems, out _) is { } profile)
                    before = Model(profile, options);
            }
            var unchanged = tree is not null && before is not null && JsonNode.DeepEquals(before, after);
            if (tree is not null)
            {
                var version = DiskVersion(path);
                if (version == 0) { _writeProblems.Add(Strings.Settings_PreFreeze); return false; }
                if (version > 1)
                {
                    if (path == FilePath) NewerVersionFile = version;
                    // An unedited newer file is simply left alone; it must not block saving the other files.
                    if (unchanged) return true;
                    _writeProblems.Add(L.F(Strings.Settings_NewerVersion, version, 1));
                    return false;
                }
            }
            tree = tree is null ? (JsonObject)after.DeepClone() : JsonMerge.Apply(tree, MatchKeys(before ?? new JsonObject(), tree), MatchKeys(after, tree));
            // Removing a string-map entry removes the rule, rather than leaving an invalid null rule.
            if (path == FilePath && before?["tools"]?["approvals"] is JsonObject oldRules && after["tools"]?["approvals"] is JsonObject newRules
                && tree.LastOrDefault(p => p.Key.Equals("tools", StringComparison.OrdinalIgnoreCase)).Value is JsonObject tools
                && tools.LastOrDefault(p => p.Key.Equals("approvals", StringComparison.OrdinalIgnoreCase)).Value is JsonObject rules)
                foreach (var key in oldRules.Select(p => p.Key).Where(key => !newRules.ContainsKey(key)).ToArray()) rules.Remove(key);
            var bytes = unchanged ? original! : Encoding.UTF8.GetBytes(tree.ToJsonString(options));
            var relative = Path.GetRelativePath(_directory, path);
            var result = _writer.Replace(path, bytes, backup, 1, _state.GetLastWritten(relative));
            if (result is SafeFileResult.Written or SafeFileResult.Unchanged)
            {
                _before[path] = (JsonObject)after.DeepClone();
                if (result == SafeFileResult.Written) _state.SetLastWritten(relative, SafeFile.Hash(bytes));
                return true;
            }
            if (result == SafeFileResult.NewerVersion)
            {
                var version = DiskVersion(path);
                if (path == FilePath) NewerVersionFile = version;
                _writeProblems.Add(L.F(Strings.Settings_NewerVersion, version, 1));
            }
            else _writeProblems.Add(L.F(result == SafeFileResult.BackupFailed ? Strings.Settings_ProblemBackupFailed : Strings.Settings_ProblemWriteFailed,
                Path.GetFileName(path), result.ToString()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _writeProblems.Add(L.F(Strings.Settings_ProblemWriteFailed, Path.GetFileName(path), ex.GetType().Name)); }
        return false;
    }

    public string ConnectionPath(string id) => ValidConnectionId(id) ? Path.Combine(ConnectionsDirectory, id + ".json")
        : throw new ArgumentException("Invalid connection id.", nameof(id));

    public static bool ValidConnectionId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || id is "." or ".." || id.EndsWith('.')
            || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-')) return false;
        var stem = id.Split('.')[0];
        return !new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
            && !(stem.Length == 4 && stem[3] is >= '1' and <= '9'
                && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
    }

    public static HotlineSettings Normalize(HotlineSettings s)
    {
        s.General ??= new(); s.Activation ??= new(); s.Window ??= new(); s.Diagnostics ??= new(); s.Chat ??= new(); s.Tools ??= new();
        s.Chat.Connections = (s.Chat.Connections ?? []).Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Id)).DistinctBy(b => b.Id, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var b in s.Chat.Connections)
        {
            if (string.IsNullOrWhiteSpace(b.Name)) b.Name = b.Id;
            if (b.MaxTokens is { } max) b.MaxTokens = max < 1 ? null : Math.Min(max, 1_000_000);
            if (b.Temperature is { } t) b.Temperature = double.IsFinite(t) ? Math.Clamp(t, 0, 2) : null;
        }
        s.Chat.Order ??= [];
        s.Chat.DefaultBackend ??= SettingsDefaults.Get<string>("chat", "defaultConnection");
        s.Chat.DefaultBackend = s.Chat.Connections.FirstOrDefault(b => b.Id.Equals(s.Chat.DefaultBackend, StringComparison.OrdinalIgnoreCase))?.Id ?? s.Chat.DefaultBackend;
        s.Tools.Approvals ??= [];
        s.Window.Anchor ??= new();
        foreach (var section in new object[] { s.Chat, s.Window, s.Window.Anchor })
            foreach (var property in section.GetType().GetProperties().Where(p => p.CanWrite))
                if (SettingsRanges.All.TryGetValue($"{section.GetType().Name}.{property.Name}", out var range) && property.GetValue(section) is { } value)
                {
                    if (value is int i) property.SetValue(section, range.Clamp(i));
                    else if (value is double d) property.SetValue(section, range.ClampFinite(d));
                }
        s.Window.MaxWidth = Math.Max(s.Window.MinWidth, s.Window.MaxWidth);
        return s;
    }
}
