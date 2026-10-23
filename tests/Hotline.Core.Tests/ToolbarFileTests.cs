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

    /// <summary>The user's file is byte-for-byte what it was, and nothing (.old, .bak, .tmp) appeared next to it.</summary>
    private void AssertUntouched(byte[] before)
    {
        Assert.Equal(before, File.ReadAllBytes(File_));
        Assert.Equal(["toolbar.json"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public void Missing_folder_is_created_and_the_defaults_are_written()
    {
        var path = Path.Combine(_root, "sub", "toolbar.json");
        var load = ToolbarFile.Load(path);
        Assert.Equal(ToolbarModel.Defaults, load.Model);
        Assert.Null(load.Error);
        Assert.Equal(ToolbarModel.Defaults.ToJson(), File.ReadAllText(path));
    }

    [Fact]
    public void V1_file_gives_the_defaults_with_a_warning_and_is_never_touched()
    {
        File.WriteAllText(File_, V1Text);
        var before = File.ReadAllBytes(File_);

        foreach (var _ in new[] { 1, 2 }) // a second start behaves the same
        {
            var load = ToolbarFile.Load(File_);

            Assert.Null(load.Error);
            Assert.Equal(ToolbarModel.Defaults.Items(Quadrant.BottomLeft), load.Model.Items(Quadrant.BottomLeft));
            Assert.Equal(ToolbarModel.Defaults.Items(Quadrant.TopLeft), load.Model.Items(Quadrant.TopLeft));
            Assert.Equal([Hotline.Core.Text.Localization.Strings.Bar_VersionOneUnsupported], load.Model.Warnings);
            AssertUntouched(before);
        }
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)] // a newer version is read as far as it is understood (with a warning), and never rewritten
    public void V2_and_newer_files_load_and_are_not_rewritten(int version, bool warns)
    {
        var text = "{ \"version\": " + version + ", \"top\": { \"left\": [\"pin\"], \"right\": [] }, \"items\": { \"left\": [\"settings\"], \"right\": [] } }";
        File.WriteAllText(File_, text);
        var before = File.ReadAllBytes(File_);

        var load = ToolbarFile.Load(File_);

        Assert.Null(load.Error);
        Assert.Equal(warns, load.Model.Warnings.Count > 0);
        Assert.Equal(["pin"], load.Model.Items(Quadrant.TopLeft));
        AssertUntouched(before);
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
        Assert.NotEmpty(load.Model.Warnings);
        AssertUntouched(before);
    }

    [Theory]
    [InlineData("")] // a zero-byte file
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
        AssertUntouched(before);
    }

    [Fact]
    public void Save_then_Load_equals_the_model_and_leaves_no_temp_file()
    {
        var model = ToolbarModel.Defaults with { Style = new ButtonStyle(ButtonSize.Wide, true, false, 30, null) };
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

        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me, FileSystemRights.CreateFiles, AccessControlType.Deny);
        var info = new DirectoryInfo(dir);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(deny);
        info.SetAccessControl(acl);
        try
        {
            var load = ToolbarFile.Load(path);
            Assert.Equal(ToolbarModel.Defaults, load.Model); // a missing file can't be created here
            Assert.Null(load.Error);
            Assert.False(File.Exists(path));
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            info.SetAccessControl(acl);
        }
    }
}
