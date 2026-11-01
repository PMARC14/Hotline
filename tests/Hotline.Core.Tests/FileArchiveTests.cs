using System.Security.AccessControl;
using System.Security.Principal;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class FileArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));

    public FileArchiveTests() => Directory.CreateDirectory(_root);

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [Fact]
    public void KeepOld_copies_to_dot_old()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "original");

        var archive = FileArchive.KeepOld(path);

        Assert.Equal(path + ".old", archive);
        Assert.Equal("original", File.ReadAllText(path + ".old"));
        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public void KeepOld_uses_old2_when_old_exists_and_keeps_the_first()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "second");
        File.WriteAllText(path + ".old", "first");

        var archive = FileArchive.KeepOld(path);

        Assert.Equal(path + ".old2", archive);
        Assert.Equal("first", File.ReadAllText(path + ".old"));
        Assert.Equal("second", File.ReadAllText(path + ".old2"));
        Assert.Equal("second", File.ReadAllText(path));
    }

    [Fact]
    public void KeepOld_missing_file_returns_null()
        => Assert.Null(FileArchive.KeepOld(Path.Combine(_root, "nope.json")));

    [Fact]
    public void KeepOld_readonly_target_folder_returns_null_without_throwing()
    {
        var dir = Path.Combine(_root, "locked");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "original");

        // Deny creating files in the folder, so the copy itself fails.
        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me, FileSystemRights.CreateFiles, AccessControlType.Deny);
        var info = new DirectoryInfo(dir);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(deny);
        info.SetAccessControl(acl);
        try
        {
            Assert.Null(FileArchive.KeepOld(path));
            Assert.Equal("original", File.ReadAllText(path));
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            info.SetAccessControl(acl);
        }
    }
}
