using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class BackupFilesTests : TempDirTest
{
    public BackupFilesTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Read_only_sources_produce_writable_backups_that_can_be_pruned()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "hand edited");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var first = BackupFiles.CopyStamped(path, new DateTime(2026, 11, 3, 12, 0, 0), keep: 1);
            Assert.Equal(FileAttributes.Normal, File.GetAttributes(first));
            var second = BackupFiles.CopyStamped(path, new DateTime(2026, 11, 3, 12, 0, 1), keep: 1);
            Assert.False(File.Exists(first));
            Assert.Equal("hand edited", File.ReadAllText(second));
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            foreach (var file in Directory.GetFiles(_dir)) File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Concurrent_copies_at_the_same_timestamp_keep_every_backup()
    {
        const int workers = 8, rounds = 8;
        var now = new DateTime(2026, 11, 1, 12, 0, 0, 123);
        var paths = Enumerable.Range(0, rounds).Select(i => Path.Combine(_dir, $"settings-{i}.json")).ToArray();
        foreach (var path in paths) File.WriteAllText(path, "hand edited settings");
        using var barrier = new Barrier(workers);
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var copies = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Factory.StartNew(() =>
        {
            foreach (var path in paths)
            {
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Backup workers did not meet");
                try { copies.Add(BackupFiles.CopyStamped(path, now, keep: workers)); }
                catch (Exception ex) { errors.Add(ex); }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(errors);
        Assert.Equal(workers * rounds, copies.Distinct().Count());
        foreach (var path in paths) Assert.Equal(workers, Directory.GetFiles(_dir, Path.GetFileName(path) + ".*.bak").Length);
        Assert.All(copies, copy => Assert.Equal("hand edited settings", File.ReadAllText(copy)));
    }

    [Fact]
    public void Only_the_newest_copies_are_kept_and_other_files_are_left_alone()
    {
        var path = Path.Combine(_dir, "mcp.json");
        File.WriteAllText(path, "{}");
        File.WriteAllText(Path.Combine(_dir, "other.json.bak-20260101-000000"), "x"); // another file's copy
        for (var i = 0; i < 15; i++) File.WriteAllText($"{path}.bak-20261022-1530{i:00}", "old");
        File.WriteAllText($"{path}.bak-20261022-153014-1", "same second, later");

        BackupFiles.Prune(path, keep: 10);

        var left = Directory.GetFiles(_dir, "mcp.json.bak-*").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(10, left.Count);
        Assert.Contains("mcp.json.bak-20261022-153014-1", left); // newest of all
        Assert.DoesNotContain("mcp.json.bak-20261022-153000", left);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(_dir, "other.json.bak-20260101-000000")));
    }

    [Fact]
    public void Fewer_copies_than_the_limit_and_missing_folders_are_fine()
    {
        var path = Path.Combine(_dir, "mcp.json");
        File.WriteAllText($"{path}.bak-20261022-153000", "one");
        BackupFiles.Prune(path, keep: 10);
        Assert.Single(Directory.GetFiles(_dir, "mcp.json.bak-*"));
        BackupFiles.Prune(Path.Combine(_dir, "nope", "mcp.json"));
        BackupFiles.Prune("mcp.json");
    }
}
