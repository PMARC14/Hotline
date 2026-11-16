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

        var reserved = paths.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, _, old) in connections)
            if (!old) renames[Path.GetFileNameWithoutExtension(path)] = Path.GetFileNameWithoutExtension(path);
        var plannedIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, tree, old) in connections)
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            var oldId = old ? Text(tree["id"]) ?? stem : stem;
            var id = stem;
            if (old)
            {
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
        var retainedKeys = plannedIds.Values.Select(SecretKeys.ApiKey).ToHashSet(StringComparer.Ordinal);

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
            if (old && !selectionReady)
            { failed.Add(Relative(path)); continue; }
            if (!TryCopyKey(secrets, oldId, id, out var oldKey))
            { failed.Add(Relative(path)); continue; }
            if (!old) { DeleteOldKey(secrets, oldKey, retainedKeys, Relative(path), failed); continue; }
            var target = Path.Combine(folder, id + ".json");
            tree.Remove("id");
            tree["version"] = 1;
            if (Text(tree["extraArgs"]) is { } args)
                tree["extraArgs"] = new JsonArray(CliArguments.SplitExtra(args).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
            var bytes = Encoding.UTF8.GetBytes(tree.ToJsonString(WriteOptions));
            if (!Write(target, bytes, 1)) continue;
            if (!path.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                try { files.DeleteFile(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failed.Add(Relative(path));
                    // Keep the old file retryable without creating a numbered duplicate on the next start.
                    try { files.DeleteFile(target); }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { failed.Add(Relative(target)); }
                    continue;
                }
            }
            DeleteOldKey(secrets, oldKey, retainedKeys, Relative(target), failed);
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

    private static bool TryCopyKey(ISecretStore secrets, string oldId, string id, out string? oldKey)
    {
        oldKey = $"connection:{oldId}:apiKey";
        var key = SecretKeys.ApiKey(id);
        if (oldKey == key) { oldKey = null; return true; }
        try
        {
            var previous = secrets.Get(oldKey);
            if (string.IsNullOrEmpty(previous) && oldKey != SecretKeys.ApiKey(oldId))
            {
                oldKey = SecretKeys.ApiKey(oldId);
                previous = secrets.Get(oldKey);
            }
            if (oldKey == key) { oldKey = null; return true; }
            if (string.IsNullOrEmpty(previous)) { oldKey = null; return true; }
            var current = secrets.Get(key);
            if (string.IsNullOrEmpty(current))
            {
                secrets.Set(key, previous);
                if (secrets.Get(key) != previous) return false;
            }
            else if (current != previous) { oldKey = null; } // keep an existing distinct credential and the old entry
            return true;
        }
        catch (Exception) { return false; }
    }

    private static void DeleteOldKey(ISecretStore secrets, string? oldKey, HashSet<string> retainedKeys, string name, HashSet<string> failed)
    {
        if (oldKey is null || retainedKeys.Contains(oldKey)) return;
        try { secrets.Set(oldKey, null); }
        catch (Exception) { failed.Add(name); }
    }
}
