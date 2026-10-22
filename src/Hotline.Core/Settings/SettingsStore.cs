using Hotline.Core.Activation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hotline.Core.Settings;

/// <summary>
/// Loads/saves settings.json. Never throws: bad content is backed up to .bad and replaced with defaults;
/// an unreadable/locked file yields in-memory defaults (the file is left untouched).
/// </summary>
public sealed class SettingsStore(string directory)
{
    public const string FileName = "settings.json";
    public const string ConnectionsFolder = "connections";

    public string ConnectionsDirectory => Path.Combine(directory, ConnectionsFolder);

    /// <summary>Connection files that couldn't be read (left untouched; never overwritten or deleted).</summary>
    public IReadOnlyList<string> BrokenConnectionFiles { get; private set; } = [];

    /// <summary>Ids whose files this store loaded or wrote: only these are ever deleted (when a connection is removed).</summary>
    private readonly HashSet<string> _ownedIds = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions ConnectionOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // each file shows what's set for that connection
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private static readonly JsonSerializerOptions Options = new()
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
            var s = JsonSerializer.Deserialize<HotlineSettings>(text, Options);
            if (s is null) return null;
            s.Chat ??= new ChatSettings();
            var legacy = HasLegacyBackends(text) ? s.Chat.Backends : null;
            var (files, broken) = ReadConnectionFiles();
            BrokenConnectionFiles = broken; // a file broken (or fixed) since start: the next save must leave it alone
            foreach (var f in files) _ownedIds.Add(f.Id); // a file added while running is ours to delete when its connection is removed
            if (legacy is not null)
                foreach (var p in legacy.Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Id) && files.All(f => !f.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase))))
                    files.Add(p);
            s.Chat.Backends = Ordered(files,
                s.Chat.Order is { Count: > 0 } order ? order : LegacyOrder(legacy));
            return Normalize(s);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    public HotlineSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            var fresh = new HotlineSettings();
            fresh.Chat.Backends = AssembleConnections(null, fresh.Chat.Order);
            Normalize(fresh);
            TrySave(fresh);
            return fresh;
        }

        try
        {
            var text = File.ReadAllText(FilePath);
            var s = JsonSerializer.Deserialize<HotlineSettings>(text, Options)
                    ?? throw new JsonException("settings.json contained null");
            s.Chat ??= new ChatSettings();
            var legacy = HasLegacyBackends(text) ? s.Chat.Backends : null;
            s.Chat.Backends = AssembleConnections(legacy, s.Chat.Order);
            Normalize(s);
            // The old inline "backends" list (moved into connection files) or newly added options rewrite the file:
            // every setting stays visible and editable there. The previous file (it may hold comments) is kept as
            // settings.json.bak. An older schemaVersion alone changes nothing: it is stamped on the next save.
            if (legacy is not null || HasMissingOptions(text, s))
            {
                try { File.Copy(FilePath, FilePath + ".bak", overwrite: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                // Connection files are written only when the old "backends" list moves into them; otherwise they
                // stay exactly as they are, byte for byte.
                if (legacy is not null) TrySave(s);
                else TrySaveMain(s);
            }
            return s;
        }
        catch (JsonException)
        {
            // Keep the user's file as it is (they can fix it; a fix applies live) plus a copy, and run on defaults
            // in memory. Earlier copies are never overwritten.
            try
            {
                var bad = FilePath + ".bad";
                if (File.Exists(bad)) bad = $"{FilePath}.{DateTime.Now:yyyyMMdd-HHmmss}.bad";
                File.Copy(FilePath, bad, overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            var fallback = new HotlineSettings();
            fallback.Chat.Backends = AssembleConnections(null, fallback.Chat.Order);
            return Normalize(fallback);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HotlineSettings();
        }
    }

    public void Save(HotlineSettings s)
    {
        Directory.CreateDirectory(directory);
        s.Chat.Order = s.Chat.Backends.Select(b => b.Id).ToList();
        SaveConnections(s.Chat.Backends);        // first: if this fails, settings.json still has the old list
        WriteAtomic(FilePath, SerializeMain(s));
    }

    /// <summary>settings.json content: everything except the connections (they have their own files).</summary>
    public static string Serialize(HotlineSettings s) => SerializeMain(s);

    /// <summary>settings.json content: everything except the connections (they have their own files).</summary>
    private static string SerializeMain(HotlineSettings s)
    {
        // Serialize to a tree and drop the list there: other threads read the live Chat.Backends meanwhile.
        var tree = JsonSerializer.SerializeToNode(s, Options)!.AsObject();
        if (tree["chat"] is System.Text.Json.Nodes.JsonObject chat) chat.Remove("backends");
        return tree.ToJsonString(Options);
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    // ---- connection files ----------------------------------------------------------------------

    public string ConnectionPath(string id) => Path.Combine(ConnectionsDirectory, SafeFileName(id) + ".json");

    private static string SafeFileName(string id)
    {
        var safe = new string(id.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray()).Trim('.');
        return safe.Length == 0 ? "connection" : safe;
    }

    private static bool HasLegacyBackends(string settingsText)
    {
        try
        {
            using var doc = JsonDocument.Parse(settingsText, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.EnumerateObject().FirstOrDefault(p => p.NameEquals("chat") || p.Name.Equals("chat", StringComparison.OrdinalIgnoreCase)).Value is { ValueKind: JsonValueKind.Object } chat
                   && chat.EnumerateObject().Any(p => p.Name.Equals("backends", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Reads every connections/*.json. The file name is the id when the file has none.</summary>
    private (List<BackendProfile> Profiles, List<string> Broken) ReadConnectionFiles()
    {
        var profiles = new List<BackendProfile>();
        var broken = new List<string>();
        if (!Directory.Exists(ConnectionsDirectory)) return (profiles, broken);
        foreach (var file in Directory.EnumerateFiles(ConnectionsDirectory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var p = JsonSerializer.Deserialize<BackendProfile>(File.ReadAllText(file), ConnectionOptions);
                if (p is null) { broken.Add(file); continue; }
                if (string.IsNullOrWhiteSpace(p.Id)) p.Id = Path.GetFileNameWithoutExtension(file);
                if (profiles.Any(x => x.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase))) { broken.Add(file); continue; } // duplicate id
                profiles.Add(p);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { broken.Add(file); }
        }
        return (profiles, broken);
    }

    /// <summary>
    /// Connections for Load: the files, or (once) the old settings.json list.
    /// </summary>
    private List<BackendProfile> AssembleConnections(List<BackendProfile>? legacy, List<string>? order)
    {
        var (files, broken) = ReadConnectionFiles();
        BrokenConnectionFiles = broken;
        foreach (var p in files) _ownedIds.Add(p.Id);
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

    /// <summary>Writes changed connection files and deletes the files of removed connections (only ones it owns).</summary>
    private void SaveConnections(List<BackendProfile> profiles)
    {
        Directory.CreateDirectory(ConnectionsDirectory);
        var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in profiles)
        {
            var path = ConnectionPath(p.Id);
            current.Add(p.Id);
            if (BrokenConnectionFiles.Any(b => string.Equals(Path.GetFullPath(b), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
                continue; // the user's unreadable file stays exactly as it is
            var json = JsonSerializer.Serialize(p, ConnectionOptions);
            if (!File.Exists(path) || File.ReadAllText(path) != json) WriteAtomic(path, json);
            _ownedIds.Add(p.Id);
        }
        var inUse = new HashSet<string>(profiles.Select(p => Path.GetFullPath(ConnectionPath(p.Id))), StringComparer.OrdinalIgnoreCase);
        foreach (var id in _ownedIds.Where(id => !current.Contains(id)).ToList())
        {
            var path = Path.GetFullPath(ConnectionPath(id));
            if (BrokenConnectionFiles.Any(b => string.Equals(Path.GetFullPath(b), path, StringComparison.OrdinalIgnoreCase)))
                continue; // unreadable right now (an editor mid-save): its connection only looks removed; keep owning it
            if (!inUse.Contains(path)) // two ids can map to one file name: never delete a file a remaining connection uses
                try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            _ownedIds.Remove(id);
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
        try { Save(s); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* keep running on in-memory settings */ }
    }

    /// <summary>Writes settings.json only, leaving every connection file as it is.</summary>
    private void TrySaveMain(HotlineSettings s)
    {
        try
        {
            s.Chat.Order = s.Chat.Backends.Select(b => b.Id).ToList();
            WriteAtomic(FilePath, SerializeMain(s));
        }
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
        s.Chat.MaxImagePixels = Math.Clamp(s.Chat.MaxImagePixels, 256, 8192);
        s.Chat.HistoryRetentionDays = Math.Clamp(s.Chat.HistoryRetentionDays, 1, 3650);
        s.Chat.ThinkingDelayMs = Math.Clamp(s.Chat.ThinkingDelayMs, 0, 10_000);
        if (!Activation.Hotkey.TryParseShortcut(s.Chat.StopShortcut, out _)) s.Chat.StopShortcut = "Esc";
        if (s.Window.FontSize is { } fontSize) s.Window.FontSize = Math.Clamp(fontSize, 10, 32);
        s.Window.Anchor ??= new PanelAnchor();
        // → v13: the vertical position became the anchor's Y.
        if (s.Window.VerticalPosition is { } vertical) { s.Window.Anchor.Y = vertical; s.Window.VerticalPosition = null; }
        if (string.IsNullOrWhiteSpace(s.Window.Anchor.Monitor)) s.Window.Anchor.Monitor = PanelAnchor.Active;
        s.Window.Anchor.X = double.IsFinite(s.Window.Anchor.X) ? Math.Clamp(s.Window.Anchor.X, 0.0, 1.0) : 0.5;
        s.Window.Anchor.Y = double.IsFinite(s.Window.Anchor.Y) ? Math.Clamp(s.Window.Anchor.Y, 0.0, 1.0) : 0.8;
        if (s.Window.Width is { } width) s.Window.Width = Math.Clamp(width, 320, 8000);
        if (s.Window.ChatHeight is { } chat) s.Window.ChatHeight = Math.Clamp(chat, 60, 8000);
        s.Window.OpenSound = string.IsNullOrWhiteSpace(s.Window.OpenSound) ? Windowing.Sounds.Off : s.Window.OpenSound.Trim();
        s.Window.CloseSound = string.IsNullOrWhiteSpace(s.Window.CloseSound) ? Windowing.Sounds.Off : s.Window.CloseSound.Trim();
        s.Window.TintOpacity = Math.Clamp(s.Window.TintOpacity, 0.0, 1.0);
        s.Window.LuminosityOpacity = Math.Clamp(s.Window.LuminosityOpacity, 0.0, 1.0);
        s.Window.WidthPercent = Math.Clamp(s.Window.WidthPercent, 20, 90);
        s.Window.MinWidth = Math.Clamp(s.Window.MinWidth, 320, 4000);
        s.Window.MaxWidth = Math.Clamp(s.Window.MaxWidth, s.Window.MinWidth, 4000);
        s.Window.Height = Math.Clamp(s.Window.Height, 0, 4000);
        s.Window.MaxHeightPercent = Math.Clamp(s.Window.MaxHeightPercent, 30, 95);
        s.SchemaVersion = HotlineSettings.CurrentSchemaVersion;
        return s;
    }
}
