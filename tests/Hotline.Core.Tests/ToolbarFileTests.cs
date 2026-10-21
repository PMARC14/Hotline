using System.Security.AccessControl;
using System.Security.Principal;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ToolbarFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_root, "toolbar.json");

    public ToolbarFileTests() => Directory.CreateDirectory(_root);

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private const string V1Text = "{\n  // rows\n  \"top\": [\"effort\", \"model\", \"provider\"],\n  \"items\": [\"pin\", \"spacer\", \"settings\"]\n}";

    [Fact]
    public void Missing_file_writes_the_defaults()
    {
        var load = ToolbarFile.Load(File_);
        Assert.Equal(ToolbarModel.Defaults, load.Model);
        Assert.Null(load.Error);
        Assert.False(load.Migrated);
        Assert.Equal(ToolbarModel.Defaults.ToJson(), File.ReadAllText(File_));
    }

    [Fact]
    public void Missing_folder_is_created()
    {
        var path = Path.Combine(_root, "sub", "toolbar.json");
        Assert.Equal(ToolbarModel.Defaults, ToolbarFile.Load(path).Model);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void V1_file_is_archived_then_rewritten_as_v2()
    {
        File.WriteAllText(File_, V1Text);
        var original = File.ReadAllBytes(File_);

        var load = ToolbarFile.Load(File_);

        Assert.True(load.Migrated);
        Assert.Null(load.Error);
        Assert.Equal(original, File.ReadAllBytes(File_ + ".old"));
        Assert.False(File.Exists(File_ + ".bak"));
        Assert.Equal(["pin"], load.Model.Items(Quadrant.BottomLeft));
        Assert.Equal(["settings"], load.Model.Items(Quadrant.BottomRight));
        Assert.Equal(["effort", "model", "provider"], load.Model.Items(Quadrant.TopLeft));
        Assert.Equal(load.Model.ToJson(), File.ReadAllText(File_));
        var again = ToolbarFile.Load(File_);
        Assert.Equal(load.Model, again.Model);
        Assert.False(again.Migrated);
    }

    [Fact]
    public void A_second_migration_keeps_the_first_archive_as_old2()
    {
        File.WriteAllText(File_, V1Text);
        ToolbarFile.Load(File_);
        var second = V1Text.Replace("settings", "newChat");
        File.WriteAllText(File_, second);

        var load = ToolbarFile.Load(File_);

        Assert.True(load.Migrated);
        Assert.Equal(V1Text, File.ReadAllText(File_ + ".old"));
        Assert.Equal(second, File.ReadAllText(File_ + ".old2"));
    }

    [Fact]
    public void V2_file_with_a_bad_value_loads_and_is_not_rewritten()
    {
        var text = ToolbarModel.Defaults.ToJson().Replace("\"size\": \"normal\"", "\"size\": 7");
        Assert.NotEqual(ToolbarModel.Defaults.ToJson(), text);
        File.WriteAllText(File_, text);
        var before = File.ReadAllBytes(File_);

        var load = ToolbarFile.Load(File_);

        Assert.Null(load.Error);
        Assert.False(load.Migrated);
        Assert.NotEmpty(load.Model.Warnings);
        Assert.Equal(before, File.ReadAllBytes(File_));
        Assert.False(File.Exists(File_ + ".old"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("{ \"version\": 2, \"items\": { \"left\": [\"pin\"")]
    [InlineData("not json at all")]
    [InlineData("[1, 2]")]
    public void Unreadable_file_sets_Error_and_is_left_alone(string text)
    {
        File.WriteAllText(File_, text);
        var before = File.ReadAllBytes(File_);

        var load = ToolbarFile.Load(File_);

        Assert.False(string.IsNullOrEmpty(load.Error));
        Assert.Equal(ToolbarModel.Defaults, load.Model);
        Assert.False(load.Migrated);
        Assert.Equal(before, File.ReadAllBytes(File_));
        Assert.False(File.Exists(File_ + ".old"));
    }

    [Fact]
    public void Zero_byte_file_sets_Error_and_is_left_alone()
    {
        File.WriteAllBytes(File_, []);
        var load = ToolbarFile.Load(File_);
        Assert.False(string.IsNullOrEmpty(load.Error));
        Assert.Equal(ToolbarModel.Defaults, load.Model);
        Assert.Empty(File.ReadAllBytes(File_));
    }

    [Fact]
    public void Newer_version_is_read_but_never_rewritten()
    {
        var text = "{ \"version\": 3, \"top\": { \"left\": [\"pin\"], \"right\": [] }, \"items\": { \"left\": [\"settings\"], \"right\": [] }, \"future\": 1 }";
        File.WriteAllText(File_, text);
        var before = File.ReadAllBytes(File_);

        var load = ToolbarFile.Load(File_);

        Assert.Null(load.Error);
        Assert.False(load.Migrated);
        Assert.Equal(["pin"], load.Model.Items(Quadrant.TopLeft));
        Assert.Equal(before, File.ReadAllBytes(File_));
        Assert.False(File.Exists(File_ + ".old"));
    }

    [Fact]
    public void Save_then_Load_equals_the_model_and_leaves_no_temp_file()
    {
        var model = ToolbarMigration.FromV1(V1Text) with { Style = new ButtonStyle(ButtonSize.Wide, true, false, 30, null) };
        ToolbarFile.Save(File_, model);
        Assert.Equal(["toolbar.json"], Directory.GetFiles(_root).Select(Path.GetFileName)); // nothing to keep the first time
        ToolbarFile.Save(File_, model);

        Assert.Equal(model, ToolbarFile.Load(File_).Model);
        Assert.Equal(["toolbar.json", "toolbar.json.bak"], Directory.GetFiles(_root).Select(Path.GetFileName).OrderBy(n => n));
    }

    [Fact]
    public void Save_keeps_the_file_it_replaces_as_bak_and_works_with_a_bare_file_name()
    {
        var handEdited = """
            // my own bar
            { "version": 2, "top": { "left": ["effort"], "right": [] } }
            """;
        File.WriteAllText(File_, handEdited);

        ToolbarFile.Save(File_, ToolbarModel.Defaults);

        Assert.Equal(handEdited, File.ReadAllText(File_ + ".bak"));
        Assert.Equal(ToolbarModel.Defaults, ToolbarFile.Load(File_).Model);

        var old = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_root);
        try { ToolbarFile.Save("bare.json", ToolbarModel.Defaults); }
        finally { Directory.SetCurrentDirectory(old); }
        Assert.True(File.Exists(Path.Combine(_root, "bare.json")));
    }

    [Fact]
    public void Readonly_folder_returns_the_model_without_throwing()
    {
        var dir = Path.Combine(_root, "locked");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "toolbar.json");
        File.WriteAllText(path, V1Text);

        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me, FileSystemRights.CreateFiles, AccessControlType.Deny);
        var info = new DirectoryInfo(dir);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(deny);
        info.SetAccessControl(acl);
        try
        {
            var load = ToolbarFile.Load(path);
            Assert.Equal(["pin"], load.Model.Items(Quadrant.BottomLeft));
            Assert.Null(load.Error);
            Assert.Equal(V1Text, File.ReadAllText(path)); // not replaced without an archive
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            info.SetAccessControl(acl);
        }
    }
}
