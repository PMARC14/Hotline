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
        File.Copy(legacySettings, newSettings);
        var legacyHistory = Path.Combine(legacyDir, "history");
        if (Directory.Exists(legacyHistory))
        {
            var newHistory = Path.Combine(dataDir, "history");
            Directory.CreateDirectory(newHistory);
            foreach (var file in Directory.EnumerateFiles(legacyHistory))
                File.Copy(file, Path.Combine(newHistory, Path.GetFileName(file)), overwrite: false);
        }
        return true;
    }
}
