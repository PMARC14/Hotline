using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Files;
using Hotline.Core.State;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Windowing;

public sealed record ToolbarLoad(ToolbarModel Model, string? Error);

/// <summary>One Settings page's toolbar snapshot, shared by all its shortcut cards.</summary>
public sealed class ToolbarShortcutSession
{
    private readonly string _path;
    private readonly IFileSystem? _files;
    private readonly StateStore? _state;
    private string? _stamp;
    public ToolbarLoad Load { get; private set; }

    public ToolbarShortcutSession(string path, IFileSystem? fileSystem = null, StateStore? state = null)
    {
        _path = path;
        _files = fileSystem;
        _state = state;
        Load = ToolbarFile.Load(path, fileSystem);
        _stamp = ToolbarFile.Fingerprint(path, fileSystem);
    }

    public (bool ChangedOnDisk, string? Error) SetShortcut(string id, string? text)
    {
        if (ToolbarFile.Fingerprint(_path, _files) != _stamp) return (true, null);
        var next = ToolbarEditor.SetShortcut(Load.Model, id, text);
        var error = ToolbarFile.Save(_path, next, _files, _state);
        if (error is not null) return (false, error);
        Load = new(next, null);
        _stamp = ToolbarFile.Fingerprint(_path, _files);
        return (false, null);
    }
}

/// <summary>Read-only loading and lossless, backed-up saves of toolbar.json v3.</summary>
public static class ToolbarFile
{
    public static ToolbarLoad Load(string path, IFileSystem? fileSystem = null)
    {
        var files = fileSystem ?? new RealFileSystem();
        try
        {
            if (!files.FileExists(path)) return new(ToolbarModel.Defaults, null);
            var warnings = new List<string>();
            var root = ToolbarModel.Parse(Encoding.UTF8.GetString(files.ReadAllBytes(path)), warnings);
            return root is JsonObject obj ? new(ToolbarModel.Resolve(obj, warnings), null) : Unreadable("the file isn't a JSON object");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return Unreadable(ex.Message); }
    }

    private static ToolbarLoad Unreadable(string reason) => new(ToolbarModel.Defaults, L.F(Strings.Bar_Unreadable, reason));

    public static string? Fingerprint(string path, IFileSystem? fileSystem = null)
    {
        var files = fileSystem ?? new RealFileSystem();
        try { return files.FileExists(path) ? SafeFile.Hash(files.ReadAllBytes(path)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Applies only typed differences to the disk tree. A no-op preserves the original bytes, including whitespace.</summary>
    public static string? Save(string path, ToolbarModel model, IFileSystem? fileSystem = null, StateStore? state = null)
    {
        try { return SaveCore(path, model, fileSystem, state); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Strings.Bar_WriteFailed; }
    }

    private static string? SaveCore(string path, ToolbarModel model, IFileSystem? fileSystem, StateStore? state)
    {
        path = Path.GetFullPath(path);
        var files = fileSystem ?? new RealFileSystem();
        var folder = Path.GetDirectoryName(path)!;
        var bytes = Encoding.UTF8.GetBytes(model.ToJson());
        if (files.FileExists(path))
        {
            var original = files.ReadAllBytes(path);
            JsonObject? tree = null;
            try { tree = ToolbarModel.Parse(Encoding.UTF8.GetString(original), []) as JsonObject; }
            catch (JsonException) { }
            if (tree is not null)
            {
                if (tree["version"] is JsonValue v && v.TryGetValue<double>(out var version) && version > ToolbarModel.CurrentVersion)
                    return L.F(Strings.Toolbar_NewerVersion, version);
                if (tree["version"] is not JsonValue old || !old.TryGetValue<int>(out var number) || number < ToolbarModel.CurrentVersion)
                    return Strings.Bar_OldVersionUnsupported;
                var before = JsonNode.Parse(ToolbarModel.Resolve(tree, []).ToJson())!.AsObject();
                var after = JsonNode.Parse(model.ToJson())!.AsObject();
                PreserveIdSpelling(tree, before, after);
                var cleared = new List<string>();
                foreach (var (id, value) in before["overrides"]!.AsObject())
                    if (!after["overrides"]!.AsObject().ContainsKey(id) && value is JsonObject)
                    {
                        // Remove the known fields through the merge while retaining any extension fields.
                        after["overrides"]![id] = new JsonObject();
                        cleared.Add(id);
                    }
                var merged = JsonMerge.Apply((JsonObject)tree.DeepClone(), before, after);
                foreach (var id in cleared)
                    if (merged["overrides"]![id] is JsonObject { Count: 0 }) merged["overrides"]!.AsObject().Remove(id);
                bytes = JsonNode.DeepEquals(tree, merged) ? original : Encoding.UTF8.GetBytes(merged.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        var relative = Path.GetFileName(path);
        var result = new SafeFile(files).Replace(path, bytes, new BackupSet(folder, BackupReason.Save, files),
            ToolbarModel.CurrentVersion, state?.GetLastWritten(relative));
        if (result == SafeFileResult.Written) state?.SetLastWritten(relative, SafeFile.Hash(bytes));
        return result switch
        {
            SafeFileResult.Written or SafeFileResult.Unchanged => null,
            SafeFileResult.NewerVersion => L.F(Strings.Toolbar_NewerVersion, ToolbarModel.CurrentVersion + 1),
            SafeFileResult.BackupFailed => L.F(Strings.Bar_SaveBackupFailed, Strings.Bar_BackupFailed),
            _ => Strings.Bar_WriteFailed,
        };
    }

    private static void PreserveIdSpelling(JsonObject tree, JsonObject before, JsonObject after)
    {
        foreach (var section in new[] { "shortcuts", "overrides", "buttons" })
        {
            if (tree[section] is not JsonObject disk) continue;
            foreach (var model in new[] { before, after })
            {
                if (model[section] is not JsonObject map) continue;
                foreach (var (id, value) in map.ToArray())
                {
                    var matches = disk.Select(p => p.Key).Where(k => k.Equals(id, StringComparison.OrdinalIgnoreCase));
                    var spelling = section == "shortcuts" ? matches.LastOrDefault() : matches.FirstOrDefault();
                    if (spelling is null || spelling == id) continue;
                    map.Remove(id);
                    map[spelling] = value;
                }
            }
        }
    }
}
