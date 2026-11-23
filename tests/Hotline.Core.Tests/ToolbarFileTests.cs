using System.Text;
using Hotline.Core.Files;
using Hotline.Core.State;
using Hotline.Core.Tests.Files;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ToolbarFileTests : TempDirTest
{
    private string File_ => Path.Combine(_dir, "toolbar.json");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{broken")]
    [InlineData("[1,2]")]
    [InlineData("{\"version\":2,\"items\":{\"left\":[\"pin\"]}}")]
    [InlineData("{\"version\":99,\"bottom\":{\"left\":[\"pin\"]}}")]
    public void Every_load_path_is_read_only(string? json)
    {
        var fs = new FakeFileSystem();
        if (json is not null) fs.Files[File_] = Encoding.UTF8.GetBytes(json);
        var load = ToolbarFile.Load(File_, fs);
        Assert.Empty(fs.Writes);
        if (json is null) Assert.Equal(ToolbarModel.Defaults, load.Model);
        else Assert.Equal(Encoding.UTF8.GetBytes(json), fs.Files[File_]);
    }

    [Theory]
    [InlineData("CopyFile")]
    [InlineData("CreateDirectory")]
    [InlineData("WriteDurable")]
    [InlineData("ReadAllBytes")]
    public void Failed_backup_or_write_preserves_original(string operation)
    {
        var fs = new FakeFileSystem();
        var bytes = Encoding.UTF8.GetBytes("{\"version\":3,\"shortcuts\":{\"pin\":\"Ctrl+Q\"}}");
        fs.Files[File_] = bytes;
        var model = ToolbarFile.Load(File_, fs).Model;
        fs.Fail = c => c.Operation == operation;
        Assert.NotNull(ToolbarFile.Save(File_, ToolbarEditor.SetShortcut(model, "pin", "Ctrl+K"), fs));
        Assert.Equal(bytes, fs.Files[File_]);
        if (operation != "WriteDurable") Assert.DoesNotContain(fs.Writes, c => c.Operation == "WriteDurable");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    public void Unsupported_version_is_never_saved_even_after_an_edit(int version)
    {
        var fs = new FakeFileSystem();
        var bytes = Encoding.UTF8.GetBytes("{\"version\":" + version + "}");
        fs.Files[File_] = bytes;
        Assert.NotNull(ToolbarFile.Save(File_, ToolbarEditor.SetShortcut(ToolbarFile.Load(File_, fs).Model, "pin", "Ctrl+Q"), fs));
        Assert.Empty(fs.Writes);
        Assert.Equal(bytes, fs.Files[File_]);
    }

    [Fact]
    public void State_hash_skips_own_backups_and_hand_edit_gets_one_save_set()
    {
        Directory.CreateDirectory(_dir);
        var state = new StateStore(Path.Combine(_dir, StateStore.FileName));
        Assert.Null(ToolbarFile.Save(File_, ToolbarModel.Defaults));
        Assert.Equal(SafeFile.Hash(File.ReadAllBytes(File_)), state.GetLastWritten("toolbar.json"));
        Assert.Null(ToolbarFile.Save(File_, ToolbarEditor.SetShortcut(ToolbarModel.Defaults, "pin", "Ctrl+Q")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "backups")));
        var edited = "// hand edit\n" + File.ReadAllText(File_);
        File.WriteAllText(File_, edited);
        var next = ToolbarEditor.SetShortcut(ToolbarFile.Load(File_).Model, "pin", "Ctrl+K");
        Assert.Null(ToolbarFile.Save(File_, next));
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(_dir, "backups"), "toolbar.json", SearchOption.AllDirectories));
        Assert.Contains("-save", backup);
        Assert.Equal(edited, File.ReadAllText(backup));
        Assert.Null(ToolbarFile.Save(File_, next));
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "backups"), "toolbar.json", SearchOption.AllDirectories));
        Assert.Equal(next, ToolbarFile.Load(File_).Model);
    }

    [Fact]
    public void Fingerprint_follows_bytes_and_missing_files_return_null()
    {
        Assert.Null(ToolbarFile.Fingerprint(File_));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "a");
        var first = ToolbarFile.Fingerprint(File_);
        Assert.NotNull(first);
        Assert.Equal(first, ToolbarFile.Fingerprint(File_));
        File.WriteAllText(File_, "b");
        Assert.NotEqual(first, ToolbarFile.Fingerprint(File_));
    }
}
