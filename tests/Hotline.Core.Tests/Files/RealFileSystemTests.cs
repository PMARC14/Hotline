using System.Text;
using Hotline.Core.Files;

namespace Hotline.Core.Tests.Files;

public sealed class RealFileSystemTests : TempDirTest
{
    [Fact]
    public void Real_adapter_publishes_and_backs_up_exact_bytes_in_a_temp_folder()
    {
        var fs = new RealFileSystem();
        var path = Path.Combine(_dir, "connections", "claude.json");
        fs.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = Encoding.UTF8.GetBytes("{\"version\":1,\"name\":\"old\"}");
        fs.WriteDurable(path, original);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var backup = new BackupSet(_dir, BackupReason.Save, fs, () => new DateTime(2026, 11, 14, 20, 0, 0));
        Assert.True(backup.TryBackup(path));
        var copy = Path.Combine(backup.DirectoryPath!, "connections", "claude.json");
        Assert.Equal(original, fs.ReadAllBytes(copy));
        Assert.False(File.GetAttributes(copy).HasFlag(FileAttributes.ReadOnly));
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        File.SetAttributes(path, FileAttributes.Normal);
        var next = Encoding.UTF8.GetBytes("{\"version\":1,\"name\":\"new\"}");
        Assert.Equal(SafeFileResult.Written, new SafeFile(fs).Replace(path, next, backup, 1));
        Assert.Equal(next, fs.ReadAllBytes(path));
        Assert.Equal(original, fs.ReadAllBytes(copy));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp", SearchOption.AllDirectories));
        Assert.Single(fs.EnumerateDirectories(Path.Combine(_dir, "backups")));
        fs.DeleteDirectory(backup.DirectoryPath!, recursive: true);
        Assert.False(fs.DirectoryExists(backup.DirectoryPath!));
    }

    [Fact]
    public void Copy_never_overwrites_an_existing_backup_and_a_failed_publish_preserves_the_destination()
    {
        var fs = new RealFileSystem();
        fs.CreateDirectory(_dir);
        var source = Path.Combine(_dir, "source");
        var copy = Path.Combine(_dir, "copy");
        fs.WriteDurable(source, [1, 2, 3]);
        fs.CopyFile(source, copy);
        fs.WriteDurable(source, [4]);
        Assert.Throws<IOException>(() => fs.CopyFile(source, copy));
        Assert.Equal(new byte[] { 1, 2, 3 }, fs.ReadAllBytes(copy));
        var blocked = Path.Combine(_dir, "blocked");
        fs.CreateDirectory(blocked);
        Assert.Equal(SafeFileResult.WriteFailed, new SafeFile(fs).Replace(blocked, [5], new BackupSet(_dir, BackupReason.Save, fs), 1));
        Assert.True(fs.DirectoryExists(blocked));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
}
