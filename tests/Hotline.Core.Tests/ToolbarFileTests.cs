using System.Security.AccessControl;
using System.Security.Principal;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ToolbarFileTests : TempDirTest
{
    private string _root => _dir;
    private string File_ => Path.Combine(_root, "toolbar.json");

    public ToolbarFileTests() => Directory.CreateDirectory(_root);

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
        var text = ToolbarModel.Defaults.ToJson().Replace("\"width\": \"normal\"", "\"width\": 7");
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

    private IEnumerable<string> Backups() =>
        Directory.GetFiles(_root, "toolbar.json.*.bak").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal)!;

    [Fact]
    public void Save_then_Load_equals_the_model_and_a_file_this_process_wrote_is_not_copied_again()
    {
        var model = ToolbarModel.Defaults with { Style = new ButtonStyle(3, true, false) };
        ToolbarFile.Save(File_, model);
        ToolbarFile.Save(File_, model);
        ToolbarFile.Save(File_, ToolbarModel.Defaults);

        Assert.Equal(ToolbarModel.Defaults, ToolbarFile.Load(File_).Model);
        Assert.Equal(["toolbar.json"], Directory.GetFiles(_root).Select(Path.GetFileName)); // every one replaced our own last write: no copies
    }

    [Fact]
    public void Save_keeps_a_hand_edited_file_once_as_a_stamped_copy_and_never_overwrites_it()
    {
        var handEdited = """
            // my own bar
            { "version": 2, "top": { "left": ["effort"], "right": [] } }
            """;
        File.WriteAllText(File_, handEdited);

        ToolbarFile.Save(File_, ToolbarModel.Defaults);
        ToolbarFile.Save(File_, ToolbarModel.Defaults with { Style = new ButtonStyle(2) });
        ToolbarFile.Save(File_, ToolbarModel.Defaults); // the editor's later edits replace Hotline's own writes

        var copy = Assert.Single(Backups());
        Assert.Matches(@"^toolbar\.json\.\d{8}-\d{6}\.bak$", copy);
        Assert.Equal(handEdited, File.ReadAllText(Path.Combine(_root, copy)));
        Assert.False(File.Exists(File_ + ".bak")); // the old rolling copy is gone
    }

    [Fact]
    public void Save_copies_a_file_edited_after_our_last_write_and_each_copy_is_kept()
    {
        ToolbarFile.Save(File_, ToolbarModel.Defaults);
        File.WriteAllText(File_, "// first edit\n" +ToolbarModel.Defaults.ToJson());
        ToolbarFile.Save(File_, ToolbarModel.Defaults);
        File.WriteAllText(File_, "// second edit\n" +ToolbarModel.Defaults.ToJson());
        ToolbarFile.Save(File_, ToolbarModel.Defaults);

        var copies = Backups().Select(n => File.ReadAllText(Path.Combine(_root, n))).ToList();
        Assert.Equal(2, copies.Count); // two names even within one second (-002)
        Assert.Contains(copies, c => c.StartsWith("// first edit"));
        Assert.Contains(copies, c => c.StartsWith("// second edit"));
    }

    [Fact]
    public void Save_keeps_only_the_newest_ten_copies()
    {
        for (var i = 0; i < 13; i++)
        {
            File.WriteAllText(File_, "// edit " + i);
            ToolbarFile.Save(File_, ToolbarModel.Defaults);
        }
        Assert.Equal(10, Backups().Count());
    }

    [Fact]
    public void Save_creates_backup_in_target_folder()
    {
        var path = Path.Combine(_root, "bare.json");
        File.WriteAllText(path, "// mine");
        ToolbarFile.Save(path, ToolbarModel.Defaults);
        Assert.True(File.Exists(path));
        Assert.Single(Directory.GetFiles(_root, "bare.json.*.bak"));
    }

    [Fact]
    public void Save_does_not_replace_the_file_when_its_copy_cannot_be_made()
    {
        var dir = Path.Combine(_root, "locked2");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "toolbar.json");
        File.WriteAllText(path, "// mine");
        var before = File.ReadAllBytes(path);

        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me, FileSystemRights.CreateFiles, AccessControlType.Deny);
        var info = new DirectoryInfo(dir);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(deny);
        info.SetAccessControl(acl);
        try
        {
            Assert.ThrowsAny<IOException>(() => ToolbarFile.Save(path, ToolbarModel.Defaults));
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            info.SetAccessControl(acl);
        }
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Fingerprint_follows_the_bytes_and_is_null_for_a_missing_file()
    {
        Assert.Null(ToolbarFile.Fingerprint(File_));
        File.WriteAllText(File_, "a");
        var a = ToolbarFile.Fingerprint(File_);
        Assert.NotNull(a);
        Assert.Equal(a, ToolbarFile.Fingerprint(File_));
        File.WriteAllText(File_, "b");
        Assert.NotEqual(a, ToolbarFile.Fingerprint(File_));
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
