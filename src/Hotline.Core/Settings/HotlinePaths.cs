namespace Hotline.Core.Settings;

/// <summary>User-visible data folder (%USERPROFILE%\.hotline) for settings, logs and history.</summary>
public static class HotlinePaths
{
    public static string DataDirectory(string userProfile) => Path.Combine(userProfile, ".hotline");

    /// <summary>
    /// One-time move from the old package-private folder: copies settings.json and history\ when the new folder
    /// has no settings yet. The old files are left in place (harmless; removed with the package).
    /// </summary>
    public static bool MigrateFromLegacy(string legacyDir, string dataDir)
    {
        var legacySettings = Path.Combine(legacyDir, SettingsStore.FileName);
        var newSettings = Path.Combine(dataDir, SettingsStore.FileName);
        if (File.Exists(newSettings) || !File.Exists(legacySettings)) return false;

        Directory.CreateDirectory(dataDir);
        // History first, file by file: an existing/locked file is skipped instead of aborting the migration.
        var legacyHistory = Path.Combine(legacyDir, "history");
        if (Directory.Exists(legacyHistory))
        {
            var newHistory = Path.Combine(dataDir, "history");
            Directory.CreateDirectory(newHistory);
            foreach (var file in Directory.EnumerateFiles(legacyHistory))
            {
                var target = Path.Combine(newHistory, Path.GetFileName(file));
                if (File.Exists(target)) continue;
                try { File.Copy(file, target); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* keep going */ }
            }
        }
        // settings.json last, via a temp file: its presence marks the migration done, so a failure retries next launch.
        var temp = newSettings + ".migrating";
        File.Copy(legacySettings, temp, overwrite: true);
        File.Move(temp, newSettings);
        return true;
    }
}
