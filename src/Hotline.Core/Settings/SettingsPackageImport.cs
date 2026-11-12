using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Settings;

public static partial class SettingsPackage
{
    /// <summary>
    /// Reads and validates the ticked categories of <paramref name="package"/> without touching the disk, keeping private
    /// copies of the planned bytes and building the execution review for any programs or tool permissions.
    /// </summary>
    public static PreparedImport PrepareImport(string dataDirectory, Stream package, IEnumerable<SettingsCategory> categories)
    {
        var loaded = Read(package);
        if (!loaded.Info.IsValid) return new([], [], "", string.Join(" ", loaded.Info.Problems.Take(3)));

        var ticked = categories.ToHashSet();
        var skipped = new List<SkippedFile>();
        var plan = new List<(string Path, string Full, byte[] Data)>();
        string root;
        try { root = Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return new([], [], "", Strings.Backup_Err_BadFolder); }

        foreach (var (path, data) in loaded.Files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var known = Classify(path)!.Value; // checked by Read
            if (!ticked.Contains(known.Category)) continue;
            if (Validate(dataDirectory, known.Category, path, data) is { } why) { skipped.Add(new(path, why)); continue; }
            string full;
            try { full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { skipped.Add(new(path, "not a usable path")); continue; }
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { skipped.Add(new(path, "outside the settings folder")); continue; }
            // A folder that is really a link could lead anywhere; its files are the user's to place, not an import's.
            if (Path.GetDirectoryName(full) is { } dir && Directory.Exists(dir) && new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
            { skipped.Add(new(path, "its folder is a link")); continue; }
            plan.Add((path, full, data.ToArray()));
        }

        return new(plan, skipped, BuildExecutionReview(root, plan));
    }

    /// <summary>
    /// Commits a <see cref="PreparedImport"/> using its validated private bytes. Refuses without writing when
    /// <see cref="PreparedImport.RequiresExecutionConfirmation"/> is true and <paramref name="executionConfirmed"/> is false.
    /// </summary>
    public static ImportResult Import(PreparedImport plan, string backupDirectory,
        bool executionConfirmed = false, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Error is { } error) return Failed(error);
        if (plan.RequiresExecutionConfirmation && !executionConfirmed)
            return Failed(Strings.Backup_ExecutionConfirmationRequired);

        var skipped = plan.Skipped.ToList();
        string? backup = null;
        var existing = plan.Files.Where(p => File.Exists(p.Full)).ToList();
        if (existing.Count > 0)
        {
            try
            {
                var stamp = $"import-{(now ?? DateTimeOffset.Now).ToLocalTime():yyyyMMdd-HHmmss}";
                backup = Path.Combine(backupDirectory, stamp);
                for (var n = 2; Directory.Exists(backup); n++) backup = Path.Combine(backupDirectory, $"{stamp}-{n}"); // never over a backup
                Directory.CreateDirectory(backup);
                foreach (var (path, full, _) in existing)
                {
                    var copy = Path.Combine(backup, path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    File.Copy(full, copy, overwrite: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return Failed(Strings.Backup_Err_BackupFailed);
            }
        }

        var written = new List<string>();
        foreach (var (path, full, data) in plan.Files)
        {
            var temp = Path.Combine(Path.GetDirectoryName(full)!, $".{Guid.NewGuid():N}.tmp");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllBytes(temp, data);
                File.Move(temp, full, overwrite: true);
                written.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new(path, "couldn't be written (in use or read-only)"));
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return new ImportResult(written, skipped, backup, null);

        static ImportResult Failed(string error) => new([], [], null, error);
    }

    /// <summary>
    /// Writes the ticked categories of a package into <paramref name="dataDirectory"/> when no execution confirmation is
    /// required; refuses without writing when the selected files contain executable configuration.
    /// </summary>
    public static ImportResult Import(string dataDirectory, Stream package, IEnumerable<SettingsCategory> categories,
        string backupDirectory, DateTimeOffset? now = null) =>
        Import(PrepareImport(dataDirectory, package, categories), backupDirectory, executionConfirmed: false, now);

    /// <summary>
    /// The connections just imported that have no API key stored on this PC (keys are never in a package), by id. Connections
    /// that run a CLI, or whose key is optional (a local server), don't count.
    /// </summary>
    public static IReadOnlyList<string> ConnectionsNeedingKey(string dataDirectory, ImportResult result, ISecretStore secrets)
    {
        var ids = new List<string>();
        foreach (var path in result.Written.Where(p => p.StartsWith("connections/", StringComparison.Ordinal)))
        {
            try
            {
                var file = Path.Combine(dataDirectory, path.Replace('/', Path.DirectorySeparatorChar));
                var profile = SettingsStore.ParseConnection(File.ReadAllText(file), path, Path.GetFileNameWithoutExtension(path), new ReadProblems(), out _);
                if (profile is null) continue;
                var id = profile.Id;
                if (ConnectionTypes.IsCli(profile.Type) || ConnectionTypes.Of(profile.Type).ApiKeyOptional) continue;
                if (string.IsNullOrEmpty(secrets.Get(SecretKeys.ApiKey(id)))) ids.Add(id);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        }
        return [.. ids.Order(StringComparer.OrdinalIgnoreCase)];
    }
}
