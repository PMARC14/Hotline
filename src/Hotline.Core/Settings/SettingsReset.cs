using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Files;
using Hotline.Core.State;
using Hotline.Core.Upgrade;

namespace Hotline.Core.Settings;

/// <summary>Explicit Reset uses one backup set and clears state. Retained connections are never rewritten.</summary>
public static class SettingsReset
{
    public const string DefaultsFolder = ".defaults", BackupsFolder = "backups";
    public static string DefaultsPath(string directory) => Path.Combine(directory, DefaultsFolder, "settings.json");
    public static string DefaultsToolbarPath(string directory) => Path.Combine(directory, DefaultsFolder, "toolbar.json");

    public static ResetResult Reset(SettingsStore store, string directory, HotlineSettings current,
        bool everything, DateTimeOffset now, IFileSystem? fileSystem = null, StateStore? state = null)
    {
        var files = fileSystem ?? new RealFileSystem();
        state ??= new StateStore(Path.Combine(directory, StateStore.FileName));
        var backup = new BackupSet(directory, BackupReason.Reset, files, () => now.LocalDateTime);
        var writer = new SafeFile(files);
        var failed = new List<string>();
        var removed = new List<string>();
        var fresh = JsonNode.Parse(StarterFiles.DefaultBytes("settings"))!.AsObject();
        if (!everything)
        {
            fresh["chat"]!["defaultConnection"] = current.Chat.DefaultBackend;
            fresh["chat"]!["order"] = new JsonArray(current.Chat.Order.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        }

        void Replace(string name, byte[] bytes, int version)
        {
            var result = writer.Replace(Path.Combine(directory, name), bytes, backup, version);
            if (result is not (SafeFileResult.Written or SafeFileResult.Unchanged)) failed.Add(name);
        }

        Replace("settings.json", Encoding.UTF8.GetBytes(fresh.ToJsonString(new JsonSerializerOptions { WriteIndented = true })), 1);
        Replace("toolbar.json", StarterFiles.DefaultBytes("toolbar"), 3);
        if (everything)
        {
            Replace("mcp.json", StarterFiles.DefaultBytes("mcp"), 1);
            try
            {
                if (files.DirectoryExists(store.ConnectionsDirectory))
                    foreach (var path in files.EnumerateFiles(store.ConnectionsDirectory, "*.json").ToArray())
                    {
                        try
                        {
                            if (SafeFile.IsNewer(files.ReadAllBytes(path), 1)) { failed.Add(Path.GetFileName(path)); continue; }
                            if (!backup.TryBackup(path)) { failed.Add(Path.GetFileName(path)); continue; }
                            files.DeleteFile(path);
                            removed.Add(Path.GetFileNameWithoutExtension(path));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(Path.GetFileName(path)); }
                    }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add("connections"); }
        }
        if (!state.Clear()) failed.Add(StateStore.FileName);
        return new(backup.DirectoryPath ?? "", removed, failed.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }
}

/// <param name="Backup">Backup folder, or empty when nothing needed backing up.</param>
/// <param name="Removed">Removed connection ids whose API keys the caller may delete.</param>
/// <param name="Failed">Files left untouched after a backup, version or write failure.</param>
public sealed record ResetResult(string Backup, IReadOnlyList<string> Removed, IReadOnlyList<string> Failed)
{
    public void Deconstruct(out string backup, out IReadOnlyList<string> removed) { backup = Backup; removed = Removed; }
}
