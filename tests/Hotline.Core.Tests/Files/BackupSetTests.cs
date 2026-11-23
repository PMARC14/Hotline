using System.Text;
using Hotline.Core.Files;

namespace Hotline.Core.Tests.Files;

public sealed class BackupSetTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hotline-fake-backups"));
    private static readonly DateTime Now = new(2026, 11, 14, 20, 0, 0);
    private static readonly string Backups = Path.Combine(Root, "backups");
    private static string Folder(string name) => Path.Combine(Backups, name);
    private static string Seed(FakeFileSystem fs, string relative, string text = "original")
    {
        var path = Path.Combine(Root, relative);
        fs.Files[path] = Encoding.UTF8.GetBytes(text);
        fs.Directories.Add(Path.GetDirectoryName(path)!);
        return path;
    }

    [Theory]
    [InlineData(BackupReason.Save, "save")]
    [InlineData(BackupReason.Reset, "reset")]
    [InlineData(BackupReason.Import, "import")]
    [InlineData(BackupReason.Upgrade, "upgrade")]
    public void One_operation_keeps_originals_and_relative_paths(BackupReason reason, string suffix)
    {
        var fs = new FakeFileSystem();
        var now = Now;
        var set = new BackupSet(Root, reason, fs, () => now);
        Assert.Empty(fs.Writes);
        var first = Seed(fs, "settings.json");
        var nested = Path.Combine("connections", "claude.json");
        var second = Seed(fs, nested, "connection");
        Assert.True(set.TryBackup(first));
        now = now.AddSeconds(2);
        Assert.True(set.TryBackup(second));
        fs.Files[first] = Encoding.UTF8.GetBytes("changed again");
        Assert.True(set.TryBackup(first));
        Assert.Equal(Folder("20261114-200000-" + suffix), set.DirectoryPath);
        Assert.Equal("original", Encoding.UTF8.GetString(fs.Files[Path.Combine(set.DirectoryPath!, "settings.json")]));
        Assert.Equal("connection", Encoding.UTF8.GetString(fs.Files[Path.Combine(set.DirectoryPath!, nested)]));
        Assert.Equal(2, fs.Writes.Count(c => c.Operation == nameof(IFileSystem.CopyFile)));
        Assert.False(set.HasFailures);
    }

    [Fact]
    public void Same_second_sets_get_numeric_suffixes_and_prune_the_oldest_saves_only()
    {
        var fs = new FakeFileSystem();
        var source = Seed(fs, "settings.json");
        var protectedNames = new[] { "reset-20200101-000000", "import-20200101-000000", "20200101-000000-reset",
            "20200101-000000-import", "20200101-000000-upgrade", "20261399-000000-save", "20200101-000000-save-not-a-number" };
        foreach (var name in protectedNames) fs.Directories.Add(Folder(name));
        foreach (var name in new[] { "settings.json.bak", "toolbar.json.old", "settings.json.bad" }) Seed(fs, name);
        var sets = Enumerable.Range(0, 23).Select(_ => new BackupSet(Root, BackupReason.Save, fs, () => Now)).ToArray();
        foreach (var set in sets) Assert.True(set.TryBackup(source));
        Assert.Equal(Folder("20261114-200000-save"), sets[0].DirectoryPath);
        Assert.Equal(Folder("20261114-200000-save-2"), sets[1].DirectoryPath);
        Assert.Equal(Folder("20261114-200000-save-23"), sets[^1].DirectoryPath);
        Assert.DoesNotContain(sets[2].DirectoryPath!, fs.Directories);
        Assert.Contains(sets[3].DirectoryPath!, fs.Directories);
        Assert.Equal(20, sets.Count(s => fs.Directories.Contains(s.DirectoryPath!)));
        Assert.All(protectedNames, name => Assert.Contains(Folder(name), fs.Directories));
        Assert.All(new[] { "settings.json.bak", "toolbar.json.old", "settings.json.bad" }, name => Assert.True(fs.Files.ContainsKey(Path.Combine(Root, name))));
    }

    [Theory]
    [InlineData(BackupReason.Save)]
    [InlineData(BackupReason.Reset)]
    [InlineData(BackupReason.Import)]
    [InlineData(BackupReason.Upgrade)]
    public void Pruning_is_chronological_and_runs_only_for_save(BackupReason reason)
    {
        var fs = new FakeFileSystem();
        var source = Seed(fs, "settings.json");
        for (var day = 1; day <= 22; day++) fs.Directories.Add(Folder($"202610{day:00}-200000-save"));
        var set = new BackupSet(Root, reason, fs, () => Now);
        Assert.True(set.TryBackup(source));
        var oldSets = fs.Directories.Count(d => Path.GetFileName(d).EndsWith("-save", StringComparison.Ordinal));
        Assert.Equal(reason == BackupReason.Save ? 20 : 22, oldSets);
        if (reason == BackupReason.Save) Assert.DoesNotContain(Folder("20261003-200000-save"), fs.Directories);
    }

    [Theory]
    [InlineData(nameof(IFileSystem.EnumerateDirectories))]
    [InlineData(nameof(IFileSystem.DeleteDirectory))]
    public void Pruning_failures_keep_the_successful_backup(string operation)
    {
        var fs = new FakeFileSystem();
        var source = Seed(fs, "settings.json");
        for (var day = 1; day <= 22; day++) fs.Directories.Add(Folder($"202610{day:00}-200000-save"));
        var enumerations = 0;
        fs.Fail = c => c.Operation == operation && (operation != nameof(IFileSystem.EnumerateDirectories) || ++enumerations > 1);
        var set = new BackupSet(Root, BackupReason.Save, fs, () => Now);
        Assert.True(set.TryBackup(source));
        Assert.True(fs.Files.ContainsKey(Path.Combine(set.DirectoryPath!, "settings.json")));
        Assert.False(set.HasFailures);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../outside.json")]
    [InlineData(".")]
    public void Paths_outside_the_data_root_are_rejected_without_writes(string relative)
    {
        var fs = new FakeFileSystem();
        var set = new BackupSet(Root, BackupReason.Save, fs, () => Now);
        Assert.Throws<ArgumentException>(() => set.TryBackup(Path.Combine(Root, relative)));
        Assert.Empty(fs.Writes);
    }
}
