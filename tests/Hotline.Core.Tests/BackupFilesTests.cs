using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class BackupFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public BackupFilesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

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
