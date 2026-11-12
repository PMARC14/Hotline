using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Windowing;

/// <summary>What <see cref="ToolbarFile.Load"/> found. <see cref="Error"/> is set when the file couldn't be read (the model is
/// then the defaults and the file is untouched).</summary>
public sealed record ToolbarLoad(ToolbarModel Model, string? Error);

/// <summary>~/.hotline/toolbar.json: reading it (creating a missing one) and writing it.</summary>
public static class ToolbarFile
{
    private static readonly Dictionary<string, (JsonObject Map, IReadOnlyList<string> Warnings)> LegacyShortcuts = new(StringComparer.OrdinalIgnoreCase);

    private static ToolbarModel WithLegacyShortcuts(string path, ToolbarModel model)
    {
        if (model.Shortcuts is not null) return model;
        var full = Path.GetFullPath(path);
        lock (LegacyShortcuts)
        {
            if (!LegacyShortcuts.TryGetValue(full, out var legacy))
            {
                var map = new JsonObject();
                var warnings = new List<string>();
                var settingsPath = Path.Combine(Path.GetDirectoryName(full)!, SettingsStore.FileName);
                try
                {
                    if (File.Exists(settingsPath))
                    {
                        var settings = ToolbarModel.Parse(File.ReadAllText(settingsPath), warnings);
                        if (settings is JsonObject root
                            && root.LastOrDefault(p => p.Key.Equals("chat", StringComparison.OrdinalIgnoreCase)).Value is JsonObject chat
                            && chat.LastOrDefault(p => p.Key.Equals("shortcuts", StringComparison.OrdinalIgnoreCase)).Value is JsonObject explicitValues)
                            map = ToolbarModel.ReadShortcutMap(explicitValues, warnings);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    // Settings reports its own read errors; they must not make a valid toolbar unreadable.
                }
                legacy = (map, warnings);
                LegacyShortcuts[full] = legacy;
            }
            return (model with { Shortcuts = (JsonObject)legacy.Map.DeepClone(), Warnings = [.. model.Warnings, .. legacy.Warnings] }).ResolveShortcuts(null);
        }
    }
    /// <summary>
    /// Never throws. A missing file is created with the defaults. Any existing file is read and never rewritten (one in the
    /// old version 1 format gives the defaults and a warning, see <see cref="ToolbarModel.Resolve"/>); an unreadable one is
    /// left untouched.
    /// </summary>
    public static ToolbarLoad Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                var initial = WithLegacyShortcuts(path, ToolbarModel.Defaults);
                TrySave(path, initial);
                return new ToolbarLoad(initial, null);
            }
            var text = File.ReadAllText(path);
            var warnings = new List<string>();
            JsonNode? root;
            try { root = ToolbarModel.Parse(text, warnings); }
            catch (JsonException ex) { return Unreadable(ex.Message); }
            if (root is not JsonObject o) return Unreadable("the file isn't a JSON object");

            return new ToolbarLoad(WithLegacyShortcuts(path, ToolbarModel.Resolve(o, warnings)), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Unreadable(ex.Message);
        }
    }

    private static ToolbarLoad Unreadable(string reason) =>
        new(ToolbarModel.Defaults, L.F(Strings.Bar_Unreadable, reason));

    /// <summary>What this process last wrote to each path (full path → the bytes), to tell its own file from a hand edit.</summary>
    private static readonly Dictionary<string, byte[]> LastWritten = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A short fingerprint of the file's bytes (SHA-256 hex), or null when it is missing or can't be read. Settings compares it
    /// before applying an edit to see whether the file changed since the page read or saved it.</summary>
    public static string? Fingerprint(string path)
    {
        try { return File.Exists(path) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Writes the model as a v2 file: a temp file in the same folder, then moved over the old one. Settings rewrites a
    /// hand-edited file from the model, which drops its comments and anything that was ignored, so a file that isn't exactly what this
    /// process last wrote there is first copied to <c>toolbar.json.&lt;yyyyMMdd-HHmmss&gt;.bak</c> (see <see cref="BackupFiles.CopyStamped"/>:
    /// never over an older copy, the newest 10 kept) unless <paramref name="keepPrevious"/> is false. If that copy can't be made nothing is
    /// replaced and an <see cref="IOException"/> says why (its message names the reason, for the notice).</summary>
    public static string? Save(string path, ToolbarModel model, bool keepPrevious = true)
    {
        path = Path.GetFullPath(path);
        if (File.Exists(path))
        {
            try
            {
                var text = File.ReadAllText(path);
                var root = ToolbarModel.Parse(text, []);
                if (root is JsonObject obj && obj["version"] is JsonValue v && v.TryGetValue<int>(out var fileVer) && fileVer > ToolbarModel.CurrentVersion)
                {
                    return L.F(Strings.Toolbar_NewerVersion, fileVer);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        if (Path.GetDirectoryName(path) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
        var bytes = new UTF8Encoding(false).GetBytes(model.ToJson());
        if (keepPrevious && File.Exists(path) && !IsOurLastWrite(path))
        {
            try { BackupFiles.CopyStamped(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(L.F(Strings.Bar_SaveBackupFailed, ex.Message), ex);
            }
        }
        DurableFile.WriteBytes(path, bytes);
        lock (LastWritten) LastWritten[path] = bytes;
        return null;
    }

    private static bool IsOurLastWrite(string path)
    {
        byte[]? ours;
        lock (LastWritten) LastWritten.TryGetValue(path, out ours);
        return ours is not null && File.ReadAllBytes(path).AsSpan().SequenceEqual(ours);
    }

    private static bool TrySave(string path, ToolbarModel model)
    {
        try { return Save(path, model, keepPrevious: false) is null; } // only used for a missing file: nothing to keep
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
