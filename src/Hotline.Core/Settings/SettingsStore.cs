using Hotline.Core.Activation;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Settings;

/// <summary>
/// Loads/saves settings.json. Never throws. It reads field by field: a bad value keeps that field's default and is listed
/// in <see cref="SettingsProblems"/>; a file that isn't a JSON object or has a syntax error is copied to .bad and runs on
/// defaults in memory (the file itself stays as it is until the user changes something in Settings). An unreadable/locked
/// file yields in-memory defaults (the file is left untouched).
/// </summary>
public sealed class SettingsStore(string directory)
{
    public const string FileName = "settings.json";
    public const string ConnectionsFolder = "connections";

    public string ConnectionsDirectory => Path.Combine(directory, ConnectionsFolder);

    /// <summary>Connection files that couldn't be read (left untouched; never overwritten or deleted).</summary>
    public IReadOnlyList<string> BrokenConnectionFiles { get; private set; } = [];

    private List<string> _mainProblems = [];
    private List<string> _connectionProblems = [];
    private List<string> _writeProblems = [];

    /// <summary>
    /// What the last read of settings.json and the connection files ignored or couldn't use (at most 50 sentences each).
    /// Empty when all is well; a successful <see cref="Save"/> clears the part about settings.json.
    /// </summary>
    public IReadOnlyList<string> SettingsProblems => [.. _mainProblems, .. _connectionProblems, .. _writeProblems];

    /// <summary>Set while settings.json as a whole can't be used (syntax error, not an object): the file is left as it is.</summary>
    public string? SettingsError { get; private set; }

    /// <summary>True when the file on disk has something Save would overwrite: a syntax error or ignored values.</summary>
    public bool SettingsFileDamaged => SettingsError is not null || _mainProblems.Count > 0;

    /// <summary>
    /// The file each connection was read from or written to (id → full path). A connection is saved back to its own file whatever
    /// its id looks like (<c>mine.json</c> holding <c>"id": "local"</c> stays <c>mine.json</c>); only a new one is named from its id.
    /// Only these files are ever deleted, and only for a connection that was removed.
    /// </summary>
    private readonly Dictionary<string, string> _sources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Files whose current content may be replaced without a copy: what this store wrote or already backed up last
    /// (full path → text). Anything else on disk (a hand edit, a file from a previous run) is copied to a .bak first.
    /// </summary>
    private readonly Dictionary<string, string> _safeToReplace = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The ids of the connections whose files this store knows (read or written).</summary>
    public IReadOnlyCollection<string> KnownConnectionIds => [.. _sources.Keys];

    /// <summary>The file a connection was read from or saved to (full path), or null when it has none yet.</summary>
    public string? SourcePath(string id) => _sources.GetValueOrDefault(id);

    /// <summary>What the last <see cref="Save"/> couldn't do (a file left as it was because it couldn't be backed up or written, two connections for one file).</summary>
    public IReadOnlyList<string> WriteProblems => _writeProblems;

    /// <summary>The time stamped into backup file names (tests fix it).</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>Replaces the copy made before a file is replaced (tests make it fail).</summary>
    public Action<string>? BackupOverride { get; set; }

    private void MakeBackup(string path)
    {
        if (BackupOverride is { } custom) custom(path);
        else BackupFiles.CopyStamped(path, Clock());
    }

    internal static readonly JsonSerializerOptions ConnectionOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // each file shows what's set for that connection
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public string FilePath => Path.Combine(directory, FileName);

    /// <summary>
    /// Reads settings.json without writing anything (no defaults, no backup, no write-back; it only notes which
    /// connection files are unreadable now): null when the file is missing, unreadable or not valid JSON — e.g.
    /// half-saved by an editor. Used for live reloads.
    /// </summary>
    public HotlineSettings? TryRead()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var text = File.ReadAllText(FilePath);
            var (s, legacy, failure, _) = Parse(text);
            if (s is null) { SettingsError = failure; return null; } // keep what is running; the problem is reported
            var (files, broken) = ReadConnectionFiles();
            BrokenConnectionFiles = broken; // a file broken (or fixed) since start: the next save must leave it alone
            if (legacy is not null)
                foreach (var p in legacy.Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Id) && files.All(f => !f.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase))))
                    files.Add(p);
            s.Chat.Backends = Ordered(files,
                s.Chat.Order is { Count: > 0 } order ? order : LegacyOrder(legacy));
            return Normalize(s);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public HotlineSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            var fresh = new HotlineSettings();
            fresh.Chat.Backends = AssembleConnections(null, fresh.Chat.Order);
            Normalize(fresh);
            try { Directory.CreateDirectory(ConnectionsDirectory); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            TrySaveMain(fresh); // connection files already there (settings.json was deleted) stay exactly as they are
            return fresh;
        }

        try
        {
            var text = File.ReadAllText(FilePath);
            var (s, legacy, failure, canRewrite) = Parse(text);
            if (s is null)
            {
                // Keep the user's file as it is (they can fix it; a fix applies live) plus a copy, and run on defaults
                // in memory. Earlier copies are never overwritten.
                SettingsError = failure;
                try
                {
                    var bad = FilePath + ".bad";
                    if (!(File.Exists(bad) && File.ReadAllText(bad) == text)) // this very text may be kept already
                    {
                        if (File.Exists(bad)) bad = $"{FilePath}.{DateTime.Now:yyyyMMdd-HHmmss}.bad";
                        File.Copy(FilePath, bad, overwrite: false);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                var fallback = new HotlineSettings();
                fallback.Chat.Backends = AssembleConnections(null, fallback.Chat.Order);
                return Normalize(fallback);
            }
            s.Chat.Backends = AssembleConnections(legacy, s.Chat.Order);
            var migrateWidths = s.SchemaVersion < 14;
            Normalize(s);
            // The old inline "backends" list (moved into connection files) or newly added options rewrite the file:
            // every setting stays visible and editable there. The previous file (it may hold comments) is first copied to
            // settings.json.<time>.bak; if that copy can't be made the file is left as it is and a problem is reported. An older
            // schemaVersion alone changes nothing, except for the pre-v14 width migration. A file with ignored values is not rewritten
            // for new options: the user's wording stays visible until they fix it.
            if (legacy is not null || (canRewrite && (migrateWidths || HasMissingOptions(text, s))))
            {
                // Connection files are written only when the old "backends" list moves into them; otherwise they
                // stay exactly as they are, byte for byte.
                if (legacy is not null) TrySave(s);
                else TrySaveMain(s);
            }
            return s;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // settings.json is locked or unreadable right now: the file stays as it is, the connections are still read and
            // the problem is reported (a later save keeps a copy of whatever is on disk by then).
            SettingsError = L.F(Strings.Settings_ProblemUnreadable, FileName, ex.GetType().Name);
            var fallback = new HotlineSettings();
            fallback.Chat.Backends = AssembleConnections(null, fallback.Chat.Order);
            return Normalize(fallback);
        }
    }

    /// <summary>
    /// settings.json text → settings, field by field (see <see cref="LenientReader"/>), plus the old inline connection list
    /// when there is one. Settings is null (and the failure given) when the text isn't a JSON object. Updates the problem list.
    /// </summary>
    private (HotlineSettings? Settings, List<BackendProfile>? Legacy, string? Failure, bool CanRewrite) Parse(string text)
    {
        var problems = new ReadProblems();
        var result = ParseSettings(text, problems, out var canRewrite);
        if (result.Settings is null) return (null, result.Legacy, result.Failure, false);
        _mainProblems = problems.All.ToList();
        SettingsError = null;
        return (result.Settings, result.Legacy, result.Failure, canRewrite);
    }

    /// <summary>
    /// The reading of settings.json text, shared by the app (<see cref="Load"/>, <see cref="TryRead"/>) and by a backup import's
    /// check, so what an import accepts is exactly what loading would accept. Nothing is stored.
    /// </summary>
    internal static (HotlineSettings? Settings, List<BackendProfile>? Legacy, string? Failure) ParseSettings(string text, ReadProblems problems)
        => ParseSettings(text, problems, out _);

    private static (HotlineSettings? Settings, List<BackendProfile>? Legacy, string? Failure) ParseSettings(string text, ReadProblems problems, out bool canRewrite)
    {
        canRewrite = false;
        if (!LenientReader.TryParse(text, FileName, out var doc, out var failure)) return (null, null, failure);
        using (doc)
        {
            var s = new HotlineSettings();
            LenientReader.Populate(doc!.RootElement, s, FileName, "", problems);
            List<BackendProfile>? legacy = null;
            if (LastMember(doc.RootElement, "chat") is { ValueKind: JsonValueKind.Object } chat && LastMember(chat, "backends") is { } backends)
            {
                if (backends.ValueKind != JsonValueKind.Array)
                    problems.Add(L.F(Strings.Settings_ProblemWrongType, $"{FileName} › chat.backends", LenientReader.Describe(backends)));
                else
                {
                    legacy = [];
                    var index = 0;
                    foreach (var item in backends.EnumerateArray())
                    {
                        var label = $"{FileName} › chat.backends[{index++}]";
                        if (item.ValueKind != JsonValueKind.Object) { problems.Add(L.F(Strings.Settings_ProblemWrongType, label, LenientReader.Describe(item))); continue; }
                        if (LenientReader.ReadProfile(item, label, problems) is { } profile) legacy.Add(profile);
                    }
                }
            }
            s.Chat.Backends = legacy ?? [];
            canRewrite = problems.All.Count == 0;
            if (s.SchemaVersion < 14 && LastMember(doc.RootElement, "window") is { ValueKind: JsonValueKind.Object } window)
                MigrateWidthUnits(s.Window, window, problems);
            return (s, legacy, null);
        }
    }

    private static void MigrateWidthUnits(WindowSettings settings, JsonElement window, ReadProblems problems)
    {
        var kept = new List<string>();
        foreach (var (key, oldDefault, currentDefault) in new[] { ("minWidth", 600, 400), ("maxWidth", 1000, 1200) })
        {
            if (LastMember(window, key) is not { ValueKind: JsonValueKind.Number } value || !value.TryGetInt32(out var number)) continue;
            if (number == oldDefault)
            {
                if (key == "minWidth") settings.MinWidth = currentDefault;
                else settings.MaxWidth = currentDefault;
            }
            else kept.Add(key);
        }
        if (settings.Width is not null) { settings.Width = null; kept.Add("width"); }
        if (settings.ChatWidth is not null) { settings.ChatWidth = null; kept.Add("chatWidth"); }
        if (kept.Count > 0) problems.Add(L.F(Strings.Settings_WidthUnitsMigrated, string.Join(", ", kept)));
    }

    /// <summary>
    /// One connection file's text → the connection, exactly as loading reads it (a missing id is the file name; the id is otherwise
    /// the file's own, whatever the file is called). Null with <paramref name="failure"/> set when the text isn't a JSON object or
    /// names an unknown connection type.
    /// </summary>
    internal static BackendProfile? ParseConnection(string text, string label, string fileStem, ReadProblems problems, out string? failure)
    {
        failure = null;
        if (!LenientReader.TryParse(text, label, out var doc, out var why)) { failure = why; return null; }
        using (doc)
        {
            var p = LenientReader.ReadProfile(doc!.RootElement, label, problems);
            if (p is null) { failure = problems.All.LastOrDefault() ?? label; return null; } // an unknown type: never guessed
            if (string.IsNullOrWhiteSpace(p.Id)) p.Id = fileStem;
            return p;
        }
    }

    /// <summary>The value of the last member with this name (any case), as JSON's duplicate rule has it; null when absent.</summary>
    private static JsonElement? LastMember(JsonElement obj, string name)
    {
        JsonElement? found = null;
        foreach (var member in obj.EnumerateObject())
            if (member.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) found = member.Value;
        return found;
    }

    /// <summary>
    /// Writes settings.json and the connection files. A file that exists and would change is first copied to
    /// <c>&lt;file&gt;.&lt;time&gt;.bak</c> unless it is what this store wrote itself; if that copy can't be made the file is left
    /// as it is and a problem is reported (<see cref="SettingsProblems"/>). <paramref name="removed"/> names the connections the
    /// user removed: only their files are deleted (null: every connection this store knows that is no longer in the list).
    /// </summary>
    public void Save(HotlineSettings s, IEnumerable<string>? removed = null)
    {
        if (SaveCore(s, removed))
        {
            _mainProblems = []; // a fresh, valid file was just written
            SettingsError = null;
        }
    }

    private bool SaveCore(HotlineSettings s, IEnumerable<string>? removed = null, bool connections = true)
    {
        _writeProblems = [];
        Directory.CreateDirectory(directory);
        s.Chat.Order = s.Chat.Backends.Select(b => b.Id).ToList();
        if (connections) SaveConnections(s.Chat.Backends, removed); // first: if this fails, settings.json still has the old list
        return ReplaceFile(FilePath, SerializeMain(s));
    }

    /// <summary>settings.json content: everything except the connections (they have their own files).</summary>
    public static string Serialize(HotlineSettings s) => SerializeMain(s);

    /// <summary>settings.json content: everything except the connections (they have their own files).</summary>
    private static string SerializeMain(HotlineSettings s)
    {
        // Serialize to a tree and drop the list there: other threads read the live Chat.Backends meanwhile.
        var tree = JsonSerializer.SerializeToNode(s, Options)!.AsObject();
        if (tree["chat"] is System.Text.Json.Nodes.JsonObject chat)
        {
            chat.Remove("backends");
            if (s.Chat.LegacyShortcutValues is { } legacy)
                chat["shortcuts"] = System.Text.Json.Nodes.JsonNode.Parse(legacy.GetRawText());
            else chat.Remove("shortcuts");
        }
        return tree.ToJsonString(Options);
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Replaces a file's content. When the file exists with other content and that isn't something this store wrote (or already
    /// copied), a timestamped .bak copy comes first; without the copy nothing is written. True when the file now holds the
    /// content; false when it was skipped (a problem is then listed).
    /// </summary>
    private bool ReplaceFile(string path, string content)
    {
        var full = Path.GetFullPath(path);
        var name = Path.GetFileName(path);
        try
        {
            if (File.Exists(path))
            {
                var current = File.ReadAllText(path);
                if (current == content) { _safeToReplace[full] = content; return true; }
                if (!(_safeToReplace.TryGetValue(full, out var safe) && safe == current))
                {
                    try { MakeBackup(path); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _writeProblems.Add(L.F(Strings.Settings_ProblemBackupFailed, name, ex.GetType().Name));
                        return false;
                    }
                }
            }
            WriteAtomic(path, content);
            _safeToReplace[full] = content;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _writeProblems.Add(L.F(Strings.Settings_ProblemWriteFailed, name, ex.GetType().Name));
            return false;
        }
    }

    // ---- connection files ----------------------------------------------------------------------

    public string ConnectionPath(string id) => Path.Combine(ConnectionsDirectory, SafeFileName(id) + ".json");

    private static string SafeFileName(string id)
    {
        var safe = new string(id.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray()).Trim('.');
        return safe.Length == 0 ? "connection" : safe;
    }

    /// <summary>Reads every connections/*.json. The file name is the id when the file has none.</summary>
    private (List<BackendProfile> Profiles, List<string> Broken) ReadConnectionFiles()
    {
        var profiles = new List<BackendProfile>();
        var broken = new List<string>();
        var problems = new ReadProblems();
        _connectionProblems = [];
        if (!Directory.Exists(ConnectionsDirectory)) return (profiles, broken);
        foreach (var file in Directory.EnumerateFiles(ConnectionsDirectory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            var label = $"{ConnectionsFolder}/{Path.GetFileName(file)}";
            try
            {
                var p = ParseConnection(File.ReadAllText(file), label, Path.GetFileNameWithoutExtension(file), problems, out var failure);
                if (p is null)
                {
                    if (failure is not null && !problems.All.Contains(failure)) problems.Add(failure);
                    broken.Add(file);
                    continue;
                }
                if (profiles.Any(x => x.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add(L.F(Strings.Settings_ProblemDuplicateId, label, LenientReader.Quote(p.Id)));
                    broken.Add(file); // duplicate id
                    continue;
                }
                profiles.Add(p);
                _sources[p.Id] = Path.GetFullPath(file); // saved back to this very file, whatever it is called
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add(L.F(Strings.Settings_ProblemUnreadable, label, ex.GetType().Name));
                broken.Add(file);
            }
        }
        _connectionProblems = problems.All.ToList();
        return (profiles, broken);
    }

    /// <summary>
    /// Connections for Load: the files, or (once) the old settings.json list.
    /// </summary>
    private List<BackendProfile> AssembleConnections(List<BackendProfile>? legacy, List<string>? order)
    {
        var (files, broken) = ReadConnectionFiles();
        BrokenConnectionFiles = broken;
        if (legacy is not null)
            foreach (var p in legacy.Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Id) && files.All(f => !f.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase))))
                files.Add(p); // moved into files on the next save
        return Ordered(files, order is { Count: > 0 } ? order : LegacyOrder(legacy));
    }

    private static List<string>? LegacyOrder(List<BackendProfile>? legacy) => legacy?.Where(p => p is not null).Select(p => p.Id).ToList();

    private static List<BackendProfile> Ordered(List<BackendProfile> profiles, List<string>? order)
    {
        // A hand edit can leave null or blank entries in the list: skip them rather than throw on a null key.
        var rank = (order ?? []).Select((id, i) => (id, i)).Where(x => !string.IsNullOrWhiteSpace(x.id)).GroupBy(x => x.id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
        return profiles.OrderBy(p => rank.TryGetValue(p.Id, out var r) ? r : int.MaxValue)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Writes changed connection files (each back to the file it was read from; a new connection is named from its id) and
    /// deletes the files of removed ones. Two connections never share a file: the one that would is not saved and a problem is listed.
    /// </summary>
    private void SaveConnections(List<BackendProfile> profiles, IEnumerable<string>? removed)
    {
        Directory.CreateDirectory(ConnectionsDirectory);
        var snapshot = profiles.ToList(); // other threads read the live list
        var alive = new HashSet<string>(snapshot.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        var gone = (removed is null ? _sources.Keys.ToList() : removed.ToList())
            .Where(id => !alive.Contains(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Who gets which file: connections with a file of their own first, then the new ones by their id.
        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var plan = new List<(BackendProfile Profile, string Path)>();
        foreach (var p in snapshot.OrderBy(p => _sources.ContainsKey(p.Id) ? 0 : 1))
        {
            var path = _sources.TryGetValue(p.Id, out var source) ? source : Path.GetFullPath(ConnectionPath(p.Id));
            var other = claimed.TryGetValue(path, out var owner) ? owner
                : _sources.Where(kv => alive.Contains(kv.Key) && !kv.Key.Equals(p.Id, StringComparison.OrdinalIgnoreCase)
                                       && kv.Value.Equals(path, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).FirstOrDefault();
            if (other is not null)
            {
                _writeProblems.Add(L.F(Strings.Settings_ProblemSharedFile, LenientReader.Quote(p.Id), LenientReader.Quote(other), Path.GetFileName(path)));
                continue;
            }
            claimed[path] = p.Id;
            plan.Add((p, path));
        }

        foreach (var (p, path) in plan)
        {
            if (BrokenConnectionFiles.Any(b => string.Equals(Path.GetFullPath(b), path, StringComparison.OrdinalIgnoreCase)))
                continue; // the user's unreadable file stays exactly as it is
            if (ReplaceFile(path, JsonSerializer.Serialize(p, ConnectionOptions))) _sources[p.Id] = path;
        }

        foreach (var id in gone)
        {
            if (!_sources.TryGetValue(id, out var path)) continue;
            if (claimed.ContainsKey(path) || BrokenConnectionFiles.Any(b => string.Equals(Path.GetFullPath(b), path, StringComparison.OrdinalIgnoreCase)))
            {
                _writeProblems.Add(L.F(Strings.Settings_ProblemDeleteFailed, Path.GetFileName(path)));
                continue; // a remaining connection uses it, or it is unreadable right now: keep owning it and keep its key
            }
            try
            {
                if (File.Exists(path)) MakeBackup(path); // even our own last save must survive an explicit removal
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _writeProblems.Add(L.F(Strings.Settings_ProblemBackupFailed, Path.GetFileName(path), ex.GetType().Name));
                continue;
            }
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _writeProblems.Add(L.F(Strings.Settings_ProblemDeleteFailed, Path.GetFileName(path)));
                continue;
            }
            _sources.Remove(id);
            _safeToReplace.Remove(path);
        }
    }

    /// <summary>True when the file lacks an option the current schema has (compared by property names, recursively).</summary>
    private static bool HasMissingOptions(string fileText, HotlineSettings s)
    {
        var docOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var file = JsonDocument.Parse(fileText, docOptions);
        using var full = JsonDocument.Parse(SerializeMain(s));
        return Missing(full.RootElement, file.RootElement);

        static bool Missing(JsonElement expected, JsonElement actual)
        {
            if (expected.ValueKind == JsonValueKind.Object)
            {
                if (actual.ValueKind != JsonValueKind.Object) return false;
                foreach (var p in expected.EnumerateObject())
                {
                    var found = actual.EnumerateObject().FirstOrDefault(a => string.Equals(a.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                    if (found.Value.ValueKind == JsonValueKind.Undefined) return p.Value.ValueKind != JsonValueKind.Null;
                    if (Missing(p.Value, found.Value)) return true;
                }
            }
            else if (expected.ValueKind == JsonValueKind.Array && actual.ValueKind == JsonValueKind.Array)
            {
                foreach (var (e, a) in expected.EnumerateArray().Zip(actual.EnumerateArray()))
                    if (Missing(e, a)) return true;
            }
            return false;
        }
    }

    private void TrySave(HotlineSettings s)
    {
        try { SaveCore(s); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* keep running on in-memory settings */ }
    }

    /// <summary>Writes settings.json only, leaving every connection file as it is.</summary>
    private void TrySaveMain(HotlineSettings s)
    {
        try { SaveCore(s, connections: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* keep running on in-memory settings */ }
    }

    public static HotlineSettings Normalize(HotlineSettings s)
    {
        s.General ??= new GeneralSettings();
        if (string.IsNullOrWhiteSpace(s.General.Language)) s.General.Language = Text.Localization.Languages.System;
        s.Activation ??= new ActivationSettings();
        s.Window ??= new WindowSettings();
        s.Diagnostics ??= new DiagnosticsSettings();
        s.Chat ??= new ChatSettings();
        // Ids match case-insensitively (as their files do): a repeated id would collide in the live merge, so the first stays.
        s.Chat.Backends = (s.Chat.Backends ?? []).Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Id))
            .DistinctBy(b => b.Id, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var b in s.Chat.Backends.Where(b => string.IsNullOrWhiteSpace(b.Name))) b.Name = b.Id;
        foreach (var b in s.Chat.Backends)
        {
            if (b.MaxTokens is { } max) b.MaxTokens = max < 1 ? null : Math.Min(max, 1_000_000);
            if (b.Temperature is { } t) b.Temperature = double.IsFinite(t) ? Math.Clamp(t, 0, 2) : null;
        }
        s.Chat.DefaultBackend = s.Chat.DefaultBackend?.Trim() ?? "";
        if (s.Chat.DefaultBackend.Length > 0) // ids match case-insensitively (as their files do); keep the connection's spelling
            s.Chat.DefaultBackend = (s.Chat.Backends.FirstOrDefault(b => b.Id == s.Chat.DefaultBackend)
                                     ?? s.Chat.Backends.FirstOrDefault(b => b.Id.Equals(s.Chat.DefaultBackend, StringComparison.OrdinalIgnoreCase))
                                     ?? s.Chat.Backends.FirstOrDefault())?.Id ?? "";
        s.Chat.MaxImagePixels = SettingsRanges.All["ChatSettings.MaxImagePixels"].Clamp(s.Chat.MaxImagePixels);
        s.Chat.HistoryRetentionDays = SettingsRanges.All["ChatSettings.HistoryRetentionDays"].Clamp(s.Chat.HistoryRetentionDays);
        s.Chat.HistoryListSize = SettingsRanges.All["ChatSettings.HistoryListSize"].Clamp(s.Chat.HistoryListSize);
        s.Chat.ThinkingDelayMs = SettingsRanges.All["ChatSettings.ThinkingDelayMs"].Clamp(s.Chat.ThinkingDelayMs);
        if (!Activation.Hotkey.TryParseShortcut(s.Chat.StopShortcut, out _)) s.Chat.StopShortcut = "Esc";
        s.Chat.Shortcuts = ChatShortcuts.Normalize(s.Chat.Shortcuts, s.Chat.StopShortcut);
        if (s.Window.FontSize is { } fontSize) s.Window.FontSize = SettingsRanges.All["WindowSettings.FontSize"].Clamp(fontSize);
        s.Window.Anchor ??= new PanelAnchor();
        // → v13: the vertical position became the anchor's Y.
        if (s.Window.VerticalPosition is { } vertical) { s.Window.Anchor.Y = vertical; s.Window.VerticalPosition = null; }
        if (string.IsNullOrWhiteSpace(s.Window.Anchor.Monitor)) s.Window.Anchor.Monitor = PanelAnchor.Active;
        s.Window.Anchor.X = SettingsRanges.All["PanelAnchor.X"].ClampFinite(s.Window.Anchor.X);
        s.Window.Anchor.Y = SettingsRanges.All["PanelAnchor.Y"].ClampFinite(s.Window.Anchor.Y);
        if (s.Window.Width is { } width) s.Window.Width = SettingsRanges.All["WindowSettings.Width"].Clamp(width);
        if (s.Window.ChatHeight is { } chat) s.Window.ChatHeight = SettingsRanges.All["WindowSettings.ChatHeight"].Clamp(chat);
        if (s.Window.ChatWidth is { } chatWidth) s.Window.ChatWidth = SettingsRanges.All["WindowSettings.ChatWidth"].Clamp(chatWidth);
        s.Window.ChatWidthPercent = SettingsRanges.All["WindowSettings.ChatWidthPercent"].Clamp(s.Window.ChatWidthPercent);
        s.Window.OpenSound = string.IsNullOrWhiteSpace(s.Window.OpenSound) ? Windowing.Sounds.Off : s.Window.OpenSound.Trim();
        s.Window.CloseSound = string.IsNullOrWhiteSpace(s.Window.CloseSound) ? Windowing.Sounds.Off : s.Window.CloseSound.Trim();
        s.Window.TintOpacity = SettingsRanges.All["WindowSettings.TintOpacity"].Clamp(s.Window.TintOpacity);
        s.Window.LuminosityOpacity = SettingsRanges.All["WindowSettings.LuminosityOpacity"].Clamp(s.Window.LuminosityOpacity);
        s.Window.WidthPercent = SettingsRanges.All["WindowSettings.WidthPercent"].Clamp(s.Window.WidthPercent);
        s.Window.MinWidth = SettingsRanges.All["WindowSettings.MinWidth"].Clamp(s.Window.MinWidth);
        s.Window.MaxWidth = Math.Max(s.Window.MinWidth, SettingsRanges.All["WindowSettings.MaxWidth"].Clamp(s.Window.MaxWidth));
        s.Window.Height = SettingsRanges.All["WindowSettings.Height"].Clamp(s.Window.Height);
        s.Window.MaxHeightPercent = SettingsRanges.All["WindowSettings.MaxHeightPercent"].Clamp(s.Window.MaxHeightPercent);
        s.SchemaVersion = HotlineSettings.CurrentSchemaVersion;
        return s;
    }
}
