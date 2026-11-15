using System.Globalization;

namespace Hotline.Core.Files;

public enum BackupReason { Save, Reset, Import, Upgrade }

/// <summary>One operation's originals, stored under a lazily created timestamped backup folder.</summary>
public sealed class BackupSet
{
    private static readonly object Gate = new();
    private readonly string _root;
    private readonly string _backups;
    private readonly IFileSystem _files;
    private readonly Func<DateTime> _clock;
    private readonly HashSet<string> _copied = new(StringComparer.OrdinalIgnoreCase);

    public BackupReason Reason { get; }
    /// <summary>Absolute path; null until a backup folder has been created.</summary>
    public string? DirectoryPath { get; private set; }
    public bool HasFailures { get; private set; }

    public BackupSet(string root, BackupReason reason, IFileSystem? fileSystem = null, Func<DateTime>? clock = null)
    {
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        _root = Path.GetFullPath(root);
        _backups = Path.Combine(_root, "backups");
        Reason = reason;
        _files = fileSystem ?? new RealFileSystem();
        _clock = clock ?? (() => DateTime.Now);
    }

    internal string RelativePath(string path)
    {
        var relative = Path.GetRelativePath(_root, Path.GetFullPath(path));
        if (relative is "." or ".." || Path.IsPathRooted(relative) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("The file must be inside the backup set's data root.", nameof(path));
        return relative;
    }

    /// <summary>Copies a file once per operation. Expected failures return false and set HasFailures.</summary>
    public bool TryBackup(string path)
    {
        path = Path.GetFullPath(path);
        var relative = RelativePath(path);
        lock (Gate)
        {
            if (_copied.Contains(path)) return true;
            try
            {
                EnsureDirectory();
                var destination = Path.Combine(DirectoryPath!, relative);
                _files.CreateDirectory(Path.GetDirectoryName(destination)!);
                _files.CopyFile(path, destination);
                _copied.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                HasFailures = true;
                return false;
            }
            if (Reason == BackupReason.Save) Prune();
            return true;
        }
    }

    private void EnsureDirectory()
    {
        if (DirectoryPath is not null) return;
        var prefix = _clock().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Reason.ToString().ToLowerInvariant();
        var last = 0;
        if (_files.DirectoryExists(_backups))
            foreach (var directory in _files.EnumerateDirectories(_backups))
                last = Math.Max(last, Sequence(Path.GetFileName(directory), prefix));
        var candidate = Path.Combine(_backups, last == 0 ? prefix : prefix + "-" + (last + 1).ToString(CultureInfo.InvariantCulture));
        while (_files.FileExists(candidate) || _files.DirectoryExists(candidate))
        {
            last = Math.Max(1, last) + 1;
            candidate = Path.Combine(_backups, prefix + "-" + last.ToString(CultureInfo.InvariantCulture));
        }
        // All in-process sets reserve their folders under Gate, including sets with different file-system adapters.
        _files.CreateDirectory(candidate);
        DirectoryPath = candidate;
    }

    private static int Sequence(string name, string prefix)
    {
        if (name == prefix) return 1;
        return name.StartsWith(prefix + "-", StringComparison.Ordinal) &&
               int.TryParse(name.AsSpan(prefix.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 2 ? n : 0;
    }

    private void Prune()
    {
        try
        {
            var saves = _files.EnumerateDirectories(_backups)
                .Select(path => (Path: path, Name: Path.GetFileName(path)))
                .Where(entry => entry.Name.Length >= 20 &&
                    DateTime.TryParseExact(entry.Name.AsSpan(0, 15), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
                    Sequence(entry.Name, entry.Name[..15] + "-save") > 0)
                .OrderByDescending(entry => entry.Name[..15], StringComparer.Ordinal)
                .ThenByDescending(entry => Sequence(entry.Name, entry.Name[..15] + "-save"))
                .Skip(20).ToArray();
            foreach (var entry in saves)
            {
                // Never delete the active operation's originals, even if the clock moved backwards.
                if (string.Equals(entry.Path, DirectoryPath, StringComparison.OrdinalIgnoreCase)) continue;
                var full = Path.GetFullPath(entry.Path);
                if (!string.Equals(Path.GetDirectoryName(full), _backups, StringComparison.OrdinalIgnoreCase)) continue;
                try { _files.DeleteDirectory(full, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
