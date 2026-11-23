using System.Text;
using Hotline.Core.Files;

namespace Hotline.Core.Tests.Files;

public sealed class SafeFileTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hotline-fake"));
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static (FakeFileSystem Fs, SafeFile Writer, BackupSet Backup, string Path) Setup(string? original)
    {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Root);
        var path = Path.Combine(Root, "settings.json");
        if (original is not null) fs.Files[path] = Bytes(original);
        return (fs, new SafeFile(fs), new BackupSet(Root, BackupReason.Save, fs, () => new DateTime(2026, 11, 14, 20, 0, 0)), path);
    }

    [Theory]
    [InlineData(null, "{\"version\":1}", SafeFileResult.Written, false)]
    [InlineData("{\"version\":1}", "{\"version\":1}", SafeFileResult.Unchanged, false)]
    [InlineData("{\"version\":2}", "{\"version\":1}", SafeFileResult.NewerVersion, false)]
    [InlineData("{\"version\":2}", "{\"version\":2}", SafeFileResult.NewerVersion, false)]
    [InlineData("{ /* comment */ \"version\":2,}", "{\"version\":1}", SafeFileResult.NewerVersion, false)]
    [InlineData("{\"version\":10000000000000000000000000000000000000000}", "{\"version\":1}", SafeFileResult.NewerVersion, false)]
    [InlineData("{\"version\":1,\"x\":0}", "{\"version\":1,\"x\":1}", SafeFileResult.Written, true)]
    [InlineData("broken json", "{\"version\":1}", SafeFileResult.Written, true)]
    public void Replace_guards_and_backs_up_before_writing(string? original, string next, SafeFileResult expected, bool copied)
    {
        var (fs, writer, backup, path) = Setup(original);
        Assert.Equal(expected, writer.Replace(path, Bytes(next), backup, version: 1));
        var copies = fs.Writes.Where(c => c.Operation == nameof(IFileSystem.CopyFile)).ToArray();
        Assert.Equal(copied ? 1 : 0, copies.Length);
        if (copied)
        {
            Assert.Equal(Bytes(original!), fs.Files[copies[0].Destination!]);
            Assert.True(fs.Calls.IndexOf(copies[0]) < fs.Calls.FindIndex(c => c.Operation == nameof(IFileSystem.WriteDurable)));
        }
        if (expected == SafeFileResult.Written) Assert.Equal(Bytes(next), fs.Files[path]);
        else { Assert.Empty(fs.Writes); Assert.Null(backup.DirectoryPath); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Last_written_hash_skips_only_a_matching_backup(bool matches)
    {
        var (fs, writer, backup, path) = Setup("{\"version\":1,\"x\":0}");
        var hash = SafeFile.Hash(matches ? fs.Files[path] : Bytes("different"));
        Assert.Equal(SafeFileResult.Written, writer.Replace(path, Bytes("{\"version\":1,\"x\":1}"), backup, 1, hash.ToLowerInvariant()));
        Assert.Equal(matches ? 0 : 1, fs.Writes.Count(c => c.Operation == nameof(IFileSystem.CopyFile)));
        if (matches) Assert.Null(backup.DirectoryPath);
    }

    [Theory]
    [InlineData(nameof(IFileSystem.FileExists), SafeFileResult.WriteFailed)]
    [InlineData(nameof(IFileSystem.ReadAllBytes), SafeFileResult.WriteFailed)]
    [InlineData(nameof(IFileSystem.DirectoryExists), SafeFileResult.BackupFailed)]
    [InlineData(nameof(IFileSystem.EnumerateDirectories), SafeFileResult.BackupFailed)]
    [InlineData(nameof(IFileSystem.CreateDirectory), SafeFileResult.BackupFailed)]
    [InlineData(nameof(IFileSystem.CopyFile), SafeFileResult.BackupFailed)]
    [InlineData(nameof(IFileSystem.WriteDurable), SafeFileResult.WriteFailed)]
    public void Expected_failures_return_results_and_preserve_original(string operation, SafeFileResult expected)
    {
        var (fs, writer, backup, path) = Setup("{\"version\":1}");
        var original = fs.Files[path].ToArray();
        fs.Directories.Add(System.IO.Path.Combine(Root, "backups"));
        fs.Fail = c => c.Operation == operation;
        Assert.Equal(expected, writer.Replace(path, Bytes("{\"version\":1,\"x\":1}"), backup, 1));
        Assert.Equal(original, fs.Files[path]);
        if (expected == SafeFileResult.BackupFailed) Assert.DoesNotContain(fs.Writes, c => c.Operation == nameof(IFileSystem.WriteDurable));
    }

    [Fact]
    public void Fake_can_fail_any_call_by_number_and_records_write_attempts()
    {
        var (baseline, writer, backup, path) = Setup("{\"version\":1}");
        var next = Bytes("{\"version\":1,\"x\":1}");
        Assert.Equal(SafeFileResult.Written, writer.Replace(path, next, backup, 1));
        for (var call = 1; call <= baseline.Calls.Count; call++)
        {
            var (fs, attempt, set, target) = Setup("{\"version\":1}");
            fs.FailOnCall = call;
            var result = attempt.Replace(target, next, set, 1);
            Assert.Contains(result, new[] { SafeFileResult.Written, SafeFileResult.BackupFailed, SafeFileResult.WriteFailed });
            Assert.Equal(fs.Calls.Where(c => c.Operation is nameof(IFileSystem.CreateDirectory) or nameof(IFileSystem.CopyFile) or
                nameof(IFileSystem.WriteDurable) or nameof(IFileSystem.DeleteDirectory)), fs.Writes);
            if (result != SafeFileResult.Written) Assert.Equal(Bytes("{\"version\":1}"), fs.Files[target]);
        }
    }

    [Fact]
    public void Utf8_bom_does_not_bypass_the_version_guard()
    {
        var (fs, writer, backup, path) = Setup(null);
        fs.Files[path] = [0xef, 0xbb, 0xbf, .. Bytes("{\"version\":2}")];
        Assert.Equal(SafeFileResult.NewerVersion, writer.Replace(path, Bytes("{\"version\":1}"), backup, 1));
        Assert.Empty(fs.Writes);
    }

    [Theory]
    [InlineData(BackupReason.Upgrade, true, SafeFileResult.Written)]
    [InlineData(BackupReason.Upgrade, false, SafeFileResult.BackupFailed)]
    [InlineData(BackupReason.Save, true, SafeFileResult.BackupFailed)]
    public void Only_an_explicit_upgrade_may_continue_without_backup(BackupReason reason, bool allow, SafeFileResult expected)
    {
        var (fs, writer, _, path) = Setup("{\"version\":1}");
        var backup = new BackupSet(Root, reason, fs);
        fs.Fail = c => c.Operation == nameof(IFileSystem.CopyFile);
        Assert.Equal(expected, writer.Replace(path, Bytes("{\"version\":1,\"x\":1}"), backup, 1, allowUnbackedUpgrade: allow));
        Assert.True(backup.HasFailures);
    }
}
