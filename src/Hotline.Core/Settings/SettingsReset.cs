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
    /// Backs up, then resets, file by file: a file that can't be copied is not deleted, one that can't be deleted is listed, and
    /// the rest goes on. Returns the backup folder, the ids of the connections removed (their API keys are the caller's to
    /// delete; always complete, whatever failed) and what failed. The caller reloads the settings afterwards.
    /// </summary>
    public static ResetResult Reset(SettingsStore store, string directory, HotlineSettings current,
        bool everything, DateTimeOffset now)
    {
        var stamp = $"reset-{now.ToLocalTime():yyyyMMdd-HHmmss}";
        var backup = Path.Combine(directory, BackupsFolder, stamp);
        for (var n = 2; Directory.Exists(backup); n++) backup = Path.Combine(directory, BackupsFolder, $"{stamp}-{n}"); // never over a backup
        Directory.CreateDirectory(backup);
        var failed = new List<string>();
        var backedUp = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // full paths with a copy in the backup folder

        bool Copy(string file, string to)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to, overwrite: true);
                backedUp.Add(Path.GetFullPath(file));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(Path.GetFileName(file));
                return false;
            }
        }

        if (File.Exists(store.FilePath)) Copy(store.FilePath, Path.Combine(backup, SettingsStore.FileName));
        var connectionFiles = Directory.Exists(store.ConnectionsDirectory)
            ? Directory.EnumerateFiles(store.ConnectionsDirectory, "*.json").ToList() : [];
        foreach (var file in connectionFiles)
            Copy(file, Path.Combine(backup, SettingsStore.ConnectionsFolder, Path.GetFileName(file)));
        foreach (var name in EverythingFiles)
            if (File.Exists(Path.Combine(directory, name))) Copy(Path.Combine(directory, name), Path.Combine(backup, name));

        var fresh = new HotlineSettings();
        IReadOnlyList<string> removed = [];
        if (everything)
        {
            var ids = current.Chat.Backends.Select(b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            fresh.Chat.Backends = [];
            var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // files still there: their connections (and keys) stay
            foreach (var file in connectionFiles)
            {
                var full = Path.GetFullPath(file);
                if (!backedUp.Contains(full) || !TryDelete(file, failed)) { kept.Add(full); continue; }
                ids.Add(Path.GetFileNameWithoutExtension(file)); // files that didn't load too: their keys go as well
            }
            ids.RemoveWhere(id => store.SourcePath(id) is { } source && kept.Contains(source)
                                  || kept.Contains(Path.GetFullPath(Path.Combine(store.ConnectionsDirectory, id + ".json"))));
            removed = ids.ToList();
            foreach (var name in EverythingFiles)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path) && backedUp.Contains(Path.GetFullPath(path))) TryDelete(path, failed); // recreated with defaults on use
            }
        }
        else
        {
            fresh.Chat.Backends = current.Chat.Backends;
            fresh.Chat.DefaultBackend = current.Chat.DefaultBackend;
            var toolbar = Path.Combine(directory, ToolbarFileName);
            if (!File.Exists(toolbar) || backedUp.Contains(Path.GetFullPath(toolbar)))
            {
                try { ToolbarFile.Save(toolbar, ToolbarModel.Defaults); } // the backup copy above has the old one
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(ToolbarFileName); }
            }
        }
        try { store.Save(SettingsStore.Normalize(fresh), removed); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(SettingsStore.FileName); }
        if (store.WriteProblems.Count > 0 && !failed.Contains(SettingsStore.FileName)) failed.Add(SettingsStore.FileName);
        return new ResetResult(backup, removed, failed.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static bool TryDelete(string file, List<string> failed)
    {
        try { File.Delete(file); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(Path.GetFileName(file)); return false; }
    }
}

/// <param name="Backup">The folder the old files were copied to ("" when it couldn't even be made).</param>
/// <param name="Removed">Ids of the connections removed: their API keys can go.</param>
/// <param name="Failed">Names of files that couldn't be copied, deleted or written (left as they were); empty when all went well.</param>
public sealed record ResetResult(string Backup, IReadOnlyList<string> Removed, IReadOnlyList<string> Failed)
{
    public void Deconstruct(out string backup, out IReadOnlyList<string> removed) { backup = Backup; removed = Removed; }
}
