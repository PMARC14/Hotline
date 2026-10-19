using Hotline.Core.Windowing;

namespace Hotline.Core.Settings;

/// <summary>
/// Settings › Advanced › Reset. Everything is copied to backups\reset-&lt;time&gt;\ first. "Settings" puts settings.json and
/// toolbar.json back to the defaults but keeps the connections (and which one is used); "everything" also removes the
/// connections, toolbar.json (recreated on the next load) and mcp.json. Prompts, quick actions, memory and history are
/// never touched. Copies of the default settings.json and toolbar.json are kept in the hidden .defaults folder (rewritten
/// at startup, so they always match this version).
/// </summary>
public static class SettingsReset
{
    public const string DefaultsFolder = ".defaults";
    public const string BackupsFolder = "backups";
    private const string ToolbarFileName = "toolbar.json";
    private static readonly string[] EverythingFiles = [ToolbarFileName, "mcp.json"];

    public static string DefaultsPath(string directory) => Path.Combine(directory, DefaultsFolder, SettingsStore.FileName);

    public static string DefaultsToolbarPath(string directory) => Path.Combine(directory, DefaultsFolder, ToolbarFileName);

    /// <summary>Writes the hidden copies of the default settings.json and toolbar.json when missing or out of date.</summary>
    public static void WriteDefaults(string directory)
    {
        var folder = Directory.CreateDirectory(Path.Combine(directory, DefaultsFolder));
        folder.Attributes |= FileAttributes.Hidden;
        var json = SettingsStore.Serialize(SettingsStore.Normalize(new HotlineSettings()));
        var path = DefaultsPath(directory);
        if (!File.Exists(path) || File.ReadAllText(path) != json) File.WriteAllText(path, json);
        var bar = ToolbarModel.Defaults.ToJson();
        var barPath = DefaultsToolbarPath(directory);
        if (!File.Exists(barPath) || File.ReadAllText(barPath) != bar) File.WriteAllText(barPath, bar);
    }

    /// <summary>
    /// Backs up, then resets. Returns the backup folder and the ids of the connections removed (their API keys are the
    /// caller's to delete). The caller reloads the settings afterwards.
    /// </summary>
    public static (string Backup, IReadOnlyList<string> Removed) Reset(SettingsStore store, string directory, HotlineSettings current,
        bool everything, DateTimeOffset now)
    {
        var stamp = $"reset-{now.ToLocalTime():yyyyMMdd-HHmmss}";
        var backup = Path.Combine(directory, BackupsFolder, stamp);
        for (var n = 2; Directory.Exists(backup); n++) backup = Path.Combine(directory, BackupsFolder, $"{stamp}-{n}"); // never over a backup
        Directory.CreateDirectory(backup);
        if (File.Exists(store.FilePath)) File.Copy(store.FilePath, Path.Combine(backup, SettingsStore.FileName), overwrite: true);
        if (Directory.Exists(store.ConnectionsDirectory))
        {
            var to = Directory.CreateDirectory(Path.Combine(backup, SettingsStore.ConnectionsFolder)).FullName;
            foreach (var file in Directory.EnumerateFiles(store.ConnectionsDirectory, "*.json"))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var name in EverythingFiles)
            if (File.Exists(Path.Combine(directory, name))) File.Copy(Path.Combine(directory, name), Path.Combine(backup, name), overwrite: true);

        var fresh = new HotlineSettings();
        IReadOnlyList<string> removed = [];
        if (everything)
        {
            var ids = current.Chat.Backends.Select(b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            fresh.Chat.Backends = [];
            if (Directory.Exists(store.ConnectionsDirectory))
                foreach (var file in Directory.EnumerateFiles(store.ConnectionsDirectory, "*.json"))
                {
                    ids.Add(Path.GetFileNameWithoutExtension(file)); // files that didn't load too: their keys go as well
                    File.Delete(file);
                }
            removed = ids.ToList();
            foreach (var name in EverythingFiles) File.Delete(Path.Combine(directory, name)); // recreated with defaults on use
        }
        else
        {
            fresh.Chat.Backends = current.Chat.Backends;
            fresh.Chat.DefaultBackend = current.Chat.DefaultBackend;
            ToolbarFile.Save(Path.Combine(directory, ToolbarFileName), ToolbarModel.Defaults); // the backup copy above has the old one
        }
        store.Save(SettingsStore.Normalize(fresh));
        return (backup, removed);
    }
}
