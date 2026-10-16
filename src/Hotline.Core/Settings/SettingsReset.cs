namespace Hotline.Core.Settings;

/// <summary>
/// Settings › Advanced › Reset. Everything is copied to backups\reset-&lt;time&gt;\ first. "Settings" puts settings.json back
/// to the defaults but keeps the connections (and which one is used); "everything" also removes the connections,
/// toolbar.json and mcp.json. Prompts, quick actions, memory and history are never touched. A copy of the default
/// settings.json is kept in the hidden .defaults folder (rewritten at startup, so it always matches this version).
/// </summary>
public static class SettingsReset
{
    public const string DefaultsFolder = ".defaults";
    public const string BackupsFolder = "backups";
    private static readonly string[] EverythingFiles = ["toolbar.json", "mcp.json"];

    public static string DefaultsPath(string directory) => Path.Combine(directory, DefaultsFolder, SettingsStore.FileName);

    /// <summary>Writes the hidden copy of the default settings.json when it's missing or out of date.</summary>
    public static void WriteDefaults(string directory)
    {
        var folder = Directory.CreateDirectory(Path.Combine(directory, DefaultsFolder));
        folder.Attributes |= FileAttributes.Hidden;
        var json = SettingsStore.Serialize(SettingsStore.Normalize(new HotlineSettings()));
        var path = DefaultsPath(directory);
        if (!File.Exists(path) || File.ReadAllText(path) != json) File.WriteAllText(path, json);
    }

    /// <summary>
    /// Backs up, then resets. Returns the backup folder and the ids of the connections removed (their API keys are the
    /// caller's to delete). The caller reloads the settings afterwards.
    /// </summary>
    public static (string Backup, IReadOnlyList<string> Removed) Reset(SettingsStore store, string directory, HotlineSettings current,
        bool everything, DateTimeOffset now)
    {
        var backup = Path.Combine(directory, BackupsFolder, $"reset-{now.ToLocalTime():yyyyMMdd-HHmmss}");
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
            removed = current.Chat.Backends.Select(b => b.Id).ToList();
            fresh.Chat.Backends = [];
            if (Directory.Exists(store.ConnectionsDirectory))
                foreach (var file in Directory.EnumerateFiles(store.ConnectionsDirectory, "*.json")) File.Delete(file);
            foreach (var name in EverythingFiles) File.Delete(Path.Combine(directory, name)); // recreated with defaults on use
        }
        else
        {
            fresh.Chat.Backends = current.Chat.Backends;
            fresh.Chat.DefaultBackend = current.Chat.DefaultBackend;
        }
        store.Save(SettingsStore.Normalize(fresh));
        return (backup, removed);
    }
}
