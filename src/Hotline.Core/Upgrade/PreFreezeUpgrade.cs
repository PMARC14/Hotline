using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Backends;
using Hotline.Core.Files;
using Hotline.Core.Settings;
using Hotline.Core.State;

namespace Hotline.Core.Upgrade;

public sealed record UpgradeResult(bool Changed, string? Backup, IReadOnlyList<string> Reset, IReadOnlyList<string> Failed);

/// <summary>Retries pre-freeze files independently. Current and newer files never mark an old file as completed.</summary>
public static class PreFreezeUpgrade
{
    private static readonly JsonDocumentOptions ReadOptions = new()
    { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, AllowDuplicateProperties = false };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static UpgradeResult Run(string directory, IFileSystem files, StateStore state, ISecretStore secrets)
    {
        directory = Path.GetFullPath(directory);
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reset = new List<string>();
        var originals = new List<string>();
        var connections = new List<(string Path, JsonObject Tree, bool Old)>();
        var backup = new BackupSet(directory, BackupReason.Upgrade, files);
        var writer = new SafeFile(files);
        var changed = false;
        string Relative(string path) => Path.GetRelativePath(directory, path).Replace('\\', '/');

        JsonObject? Read(string path)
        {
            try
            {
                if (!files.FileExists(path)) return null;
                var text = Encoding.UTF8.GetString(files.ReadAllBytes(path)).TrimStart('\uFEFF');
                return JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject ?? throw new JsonException();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { failed.Add(Relative(path)); return null; }
        }

        var settingsPath = Path.Combine(directory, "settings.json");
        var settings = Read(settingsPath);
        var oldSettings = settings is not null && !settings.ContainsKey("version");
        if (oldSettings) originals.Add(settingsPath);
        var toolbarPath = Path.Combine(directory, "toolbar.json");
        var toolbar = Read(toolbarPath);
        var oldToolbar = toolbar is not null && Version(toolbar) < 3;
        if (oldToolbar) originals.Add(toolbarPath);
        var mcpPath = Path.Combine(directory, "mcp.json");
        var mcp = Read(mcpPath);
        var oldMcp = mcp is not null && Version(mcp) <= 1 && mcp.ContainsKey("approvals");
        if (oldMcp) originals.Add(mcpPath);
        var folder = Path.Combine(directory, SettingsStore.ConnectionsFolder);
        var paths = new List<string>();
        try
        {
            if (files.DirectoryExists(folder)) paths.AddRange(files.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add("connections"); }
        foreach (var path in paths)
        {
            var tree = Read(path);
            if (tree is null || Version(tree) > 1) continue;
            var old = tree.ContainsKey("id") || !tree.ContainsKey("version");
            connections.Add((path, tree, old));
            if (old) originals.Add(path);
        }
        var markers = new[] { (".first-run-shown", ShownFlag.FirstRun), (".support-note-shown", ShownFlag.SupportNote) };
        foreach (var (name, _) in markers)
        {
            try { if (files.FileExists(Path.Combine(directory, name))) originals.Add(Path.Combine(directory, name)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(name); }
        }
        // Every original is copied before any configuration, state or Locker entry changes.
        foreach (var path in originals) backup.TryBackup(path);

        bool Write(string path, byte[] bytes, int version)
        {
            var result = writer.Replace(path, bytes, backup, version, allowUnbackedUpgrade: true);
            if (result is not (SafeFileResult.Written or SafeFileResult.Unchanged))
            { failed.Add(Relative(path)); return false; }
            if (result == SafeFileResult.Written)
            {
                changed = true;
                state.SetLastWritten(Relative(path), SafeFile.Hash(bytes));
            }
            return true;
        }

        bool WrittenByUs(string path)
        {
            try { return state.GetLastWritten(Relative(path)) == SafeFile.Hash(files.ReadAllBytes(path)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }

        var reserved = paths.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, _, old) in connections)
            if (!old) renames[Path.GetFileNameWithoutExtension(path)] = Path.GetFileNameWithoutExtension(path);
        var plannedIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var converted = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        var resumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // sources whose v1 file was already written
        foreach (var (path, tree, old) in connections)
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            var oldId = old ? Text(tree["id"]) ?? stem : stem;
            var id = stem;
            if (old)
            {
                converted[path] = ToV1(tree);
                // A start that stopped after writing the new file but before deleting this one: finish that rename. Only a
                // file this upgrade wrote (its hash in state.json) counts; an identical connection of the user's own stays.
                var basis = MakeId(oldId, []);
                var written = connections.FirstOrDefault(c => !c.Old
                    && Path.GetFileNameWithoutExtension(c.Path).Equals(basis, StringComparison.OrdinalIgnoreCase));
                if (written.Path is not null && !resumed.Contains(written.Path) && JsonNode.DeepEquals(written.Tree, converted[path])
                    && WrittenByUs(written.Path))
                {
                    resumed.Add(written.Path);
                    resumed.Add(path);
                    id = Path.GetFileNameWithoutExtension(written.Path);
                    plannedIds[path] = id;
                    renames[oldId] = id;
                    renames.TryAdd(stem, id);
                    continue;
                }
                reserved.Remove(stem);
                id = MakeId(oldId, reserved);
                reserved.Add(stem); // a failed delete must never let another connection overwrite this original
                reserved.Add(id);
            }
            plannedIds[path] = id;
            if (old)
            {
                renames.TryAdd(oldId, id);
                renames.TryAdd(stem, id);
            }
        }
        // Credential Locker matches names without case, so a key differing only by case is the same entry.
        var retainedKeys = plannedIds.Values.Select(SecretKeys.ApiKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oldKeys = new List<(string Key, string Name)>();
        var neededKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // old keys a connection left for the next start still needs

        var selectionReady = !failed.Contains("settings.json") && !failed.Any(p => p == "connections" || p.StartsWith("connections/", StringComparison.OrdinalIgnoreCase));
        if (selectionReady && settings is not null && Version(settings) <= 1)
        {
            var next = oldSettings ? JsonNode.Parse(StarterFiles.DefaultBytes("settings"))!.AsObject() : (JsonObject)settings.DeepClone();
            var source = settings["chat"] as JsonObject;
            var chat = next["chat"] as JsonObject;
            string Rename(string id) => renames.GetValueOrDefault(id, id);
            var selection = Text(source?["defaultConnection"]) ?? (oldSettings ? Text(source?["defaultBackend"]) : null);
            if (chat is not null)
            {
                if (selection is not null) chat["defaultConnection"] = Rename(selection);
                if (source?["order"] is JsonArray order)
                    chat["order"] = new JsonArray(order.Select(Text).Where(s => s is not null).Select(s => (JsonNode?)JsonValue.Create(Rename(s!))).ToArray());
            }
            if (oldSettings || !JsonNode.DeepEquals(settings, next))
            {
                // Publish selection aliases before removing their old connection files. A failed settings write retries both.
                if (!oldSettings) backup.TryBackup(settingsPath);
                selectionReady = Write(settingsPath, Encoding.UTF8.GetBytes(next.ToJsonString(WriteOptions)), 1);
                if (selectionReady && oldSettings) reset.Add("settings.json");
            }
        }
        else if (oldSettings) failed.Add("settings.json");
        if (settings is not null && Version(settings) > 1 && connections.Any(c => c.Old)) selectionReady = false;
        foreach (var (path, tree, old) in connections)
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            var oldId = old ? Text(tree["id"]) ?? stem : stem;
            var id = plannedIds[path];
            void Retry()
            {
                failed.Add(Relative(path));
                neededKeys.Add($"connection:{oldId}:apiKey");
                neededKeys.Add(SecretKeys.ApiKey(oldId));
            }
            if (old && !selectionReady) { Retry(); continue; }
            if (!TryCopyKey(secrets, oldId, id, out var oldKey)) { Retry(); continue; }
            if (!old)
            {
                if (oldKey is not null) oldKeys.Add((oldKey, Relative(path)));
                continue;
            }
            var target = Path.Combine(folder, id + ".json");
            if (!resumed.Contains(path) && !Write(target, Encoding.UTF8.GetBytes(converted[path].ToJsonString(WriteOptions)), 1))
            { Retry(); continue; }
            if (!path.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                try { files.DeleteFile(path); changed = true; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Retry();
                    // Keep the old file retryable without creating a numbered duplicate on the next start.
                    try { files.DeleteFile(target); }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { failed.Add(Relative(target)); }
                    continue;
                }
            }
            if (oldKey is not null) oldKeys.Add((oldKey, Relative(target)));
        }
        // Old entries go only after every connection has its copy: two connections can share one old key.
        foreach (var (key, name) in oldKeys)
        {
            if (retainedKeys.Contains(key) || neededKeys.Contains(key)) continue;
            try { secrets.Set(key, null); }
            catch (Exception) { failed.Add(name); }
        }
        if (oldToolbar && Write(toolbarPath, StarterFiles.DefaultBytes("toolbar"), 3)) reset.Add("toolbar.json");
        if (oldMcp)
        {
            mcp!.Remove("approvals");
            mcp["version"] = 1;
            Write(mcpPath, Encoding.UTF8.GetBytes(mcp.ToJsonString(WriteOptions)), 1);
        }
        foreach (var (name, flag) in markers)
        {
            var path = Path.Combine(directory, name);
            if (!originals.Contains(path)) continue;
            if (!state.MarkShown(flag)) { failed.Add(name); continue; }
            try { files.DeleteFile(path); changed = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(name); }
        }
        var backupPath = backup.HasFailures || backup.DirectoryPath is null ? null : Path.GetRelativePath(directory, backup.DirectoryPath);
        if (changed)
        {
            // A retry before the notice was seen: keep the first run's folder (it holds the replaced settings), or its
            // "no backup", so the notice never claims more than was saved.
            if (state.GetUpgrade() is { } earlier && !state.IsShown(ShownFlag.UpgradeNotice))
                backupPath = earlier.Backup is null || backupPath is null ? null : earlier.Backup;
            state.RecordUpgrade(backupPath);
            state.SetShown(ShownFlag.UpgradeNotice, null);
        }
        return new(changed, backupPath, reset, failed.ToList());
    }

    private static double Version(JsonObject tree) => tree["version"] is JsonValue v && v.TryGetValue<double>(out var n) ? n : 0;
    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;

    private static string MakeId(string oldId, HashSet<string?> reserved)
    {
        var basis = new string(oldId.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray()).TrimEnd('.');
        if (basis.Length == 0) basis = "connection";
        if (basis.Length > 64) basis = basis[..64].TrimEnd('.');
        if (!SettingsStore.ValidConnectionId(basis)) basis = "_" + basis[..Math.Min(basis.Length, 63)];
        var id = basis;
        for (var n = 2; reserved.Contains(id); n++)
        {
            var suffix = "-" + n;
            id = basis[..Math.Min(basis.Length, 64 - suffix.Length)] + suffix;
        }
        return id;
    }

    private static JsonObject ToV1(JsonObject tree)
    {
        var next = (JsonObject)tree.DeepClone();
        next.Remove("id");
        next["version"] = 1;
        if (Text(next["extraArgs"]) is { } args)
            next["extraArgs"] = new JsonArray(CliArguments.SplitExtra(args).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        return next;
    }

    /// <summary>
    /// Copies the old key to the lower-cased id and reads it back. False when the Locker can't be read or the copy doesn't
    /// read back. <paramref name="oldKey"/> is the entry that may be deleted afterwards, or null.
    /// </summary>
    private static bool TryCopyKey(ISecretStore secrets, string oldId, string id, out string? oldKey)
    {
        oldKey = $"connection:{oldId}:apiKey";
        var key = SecretKeys.ApiKey(id);
        if (oldKey == key) { oldKey = null; return true; }
        try
        {
            if (!secrets.TryGet(oldKey, out var previous)) return false;
            if (string.IsNullOrEmpty(previous) && oldKey != SecretKeys.ApiKey(oldId))
            {
                oldKey = SecretKeys.ApiKey(oldId);
                if (!secrets.TryGet(oldKey, out previous)) return false;
            }
            if (oldKey == key) { oldKey = null; return true; }
            if (string.IsNullOrEmpty(previous)) { oldKey = null; return true; }
            if (!secrets.TryGet(key, out var current)) return false;
            if (string.IsNullOrEmpty(current))
            {
                secrets.Set(key, previous);
                if (!secrets.TryGet(key, out var copy) || copy != previous) return false;
            }
            else if (current != previous) { oldKey = null; } // keep an existing distinct credential and the old entry
            return true;
        }
        catch (Exception) { return false; }
    }
}
