using System.Security.AccessControl;
using System.Security.Principal;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class FileArchiveTests : TempDirTest
{

    public FileArchiveTests() => Directory.CreateDirectory(_dir);

    [Theory]
    [InlineData(false, ".old")]
    [InlineData(true, ".old2")]
    public void KeepOld_copies_to_the_first_free_old_name_and_keeps_earlier_ones(bool oldExists, string extension)
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "current");
        if (oldExists) File.WriteAllText(path + ".old", "first");

        var archive = FileArchive.KeepOld(path);

        Assert.Equal(path + extension, archive);
        if (oldExists) Assert.Equal("first", File.ReadAllText(path + ".old"));
        Assert.Equal("current", File.ReadAllText(path + extension));
        Assert.Equal("current", File.ReadAllText(path));
    }

    [Fact]
    public void KeepOld_missing_file_returns_null()
        => Assert.Null(FileArchive.KeepOld(Path.Combine(_dir, "nope.json")));

    [Fact]
    public void KeepOld_readonly_target_folder_returns_null_without_throwing()
    {
        var dir = Path.Combine(_dir, "locked");
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
