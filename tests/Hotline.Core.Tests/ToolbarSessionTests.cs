using System.Text;
using System.Text.Json.Nodes;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ToolbarSessionTests : TempDirTest
{
    /// <summary>A 16x16 PNG saved by a real encoder.</summary>
    private static readonly byte[] Real16 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAVSURBVDhPY2AYXuA/GI2CUTAKyAEAeu0B/zEZFcQAAAAASUVORK5CYII=");

    private string ToolbarPath => Path.Combine(_dir, "toolbar.json");
    private string Icons => Path.Combine(_dir, "icons");
    private string Disk => File.ReadAllText(ToolbarPath);

    private ToolbarSession Open(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ToolbarPath, json);
        return new ToolbarSession(ToolbarPath);
    }

    private string Source(byte[] bytes)
    {
        var path = Path.Combine(_dir, "picked.png");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static ToolbarModel AddButton(ToolbarModel m, string id) =>
        ToolbarEditor.AddCustom(m, new CustomButton(id, id, ToolbarModel.DefaultIcon, null, "hi", false, id + ".png"));

    [Fact]
    public void Edit_saves_and_later_edits_build_on_the_saved_model()
    {
        var session = Open("""{"version":3}""");
        Assert.Equal(ToolbarSaveStatus.Saved, session.SetShortcut("pin", "Ctrl+Q").Status);
        Assert.Equal(ToolbarSaveStatus.Saved, session.SetShortcut("recent", "Ctrl+K").Status);
        Assert.Equal("Ctrl+Q", session.Model.ShortcutText("pin"));
        var tree = JsonNode.Parse(Disk)!["shortcuts"]!;
        Assert.Equal("Ctrl+Q", tree["pin"]!.GetValue<string>());
        Assert.Equal("Ctrl+K", tree["recent"]!.GetValue<string>());
    }

    [Fact]
    public void A_no_op_edit_is_unchanged_and_writes_nothing()
    {
        var session = Open("// kept\n{\"version\":3}");
        var before = File.ReadAllBytes(ToolbarPath);
        Assert.Equal(ToolbarSaveStatus.Unchanged, session.Edit(m => m).Status);
        Assert.Equal(before, File.ReadAllBytes(ToolbarPath));
        Assert.False(Directory.Exists(Path.Combine(_dir, "backups")));
    }

    [Fact]
    public void An_outside_edit_is_reported_and_not_overwritten_until_reload()
    {
        var session = Open("""{"version":3}""");
        File.WriteAllText(ToolbarPath, "// outside\n{\"version\":3,\"shortcuts\":{\"pin\":\"Ctrl+J\"}}");
        Assert.True(session.ChangedOnDisk);
        Assert.Equal(ToolbarSaveStatus.ChangedOnDisk, session.Edit(m => ToolbarEditor.SetShortcut(m, "recent", "Ctrl+K")).Status);
        Assert.StartsWith("// outside", Disk);
        var fresh = session.Reload();
        Assert.Equal("Ctrl+J", fresh.Model.ShortcutText("pin"));
        Assert.False(session.ChangedOnDisk);
        Assert.Equal(ToolbarSaveStatus.Saved, session.SetShortcut("recent", "Ctrl+K").Status);
    }

    [Fact]
    public void SetShortcut_checks_the_file_before_the_edit_but_Edit_checks_the_edit_first()
    {
        var session = Open("""{"version":3}""");
        File.WriteAllText(ToolbarPath, """{"version":3,"shortcuts":{"pin":"Ctrl+Q"}}""");
        Assert.Equal(ToolbarSaveStatus.Unchanged, session.Edit(m => m).Status);
        Assert.Equal(ToolbarSaveStatus.ChangedOnDisk, session.SetShortcut("pin", null).Status);
    }

    [Fact]
    public void A_newer_or_unreadable_file_is_never_written_and_Edit_refuses_an_unusable_load()
    {
        var newer = Open("""{"version":99}""");
        var before = File.ReadAllBytes(ToolbarPath);
        var result = newer.SetShortcut("pin", "Ctrl+Q");
        Assert.Equal(ToolbarSaveStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal(before, File.ReadAllBytes(ToolbarPath));

        var broken = Open("{broken");
        Assert.NotNull(broken.Error);
        Assert.Equal(ToolbarSaveStatus.Failed, broken.Edit(m => AddButton(m, "x")).Status);
        Assert.Equal("{broken", Disk);
    }

    [Fact]
    public void ResetLayout_keeps_shortcuts_unknown_keys_and_works_on_an_unreadable_file()
    {
        var session = Open("""{"version":3,"future":1,"shortcuts":{"pin":"Ctrl+K"},"bottom":{"left":["settings"]}}""");
        Assert.Equal(ToolbarSaveStatus.Saved, session.ResetLayout().Status);
        var tree = JsonNode.Parse(Disk)!;
        Assert.Equal("Ctrl+K", tree["shortcuts"]!["pin"]!.GetValue<string>());
        Assert.Equal(1, tree["future"]!.GetValue<int>());
        Assert.False(session.ChangedOnDisk);

        File.WriteAllText(ToolbarPath, "{broken");
        Assert.Equal(ToolbarSaveStatus.Saved, session.ResetLayout().Status);
        Assert.Null(session.Error);
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "backups"), "toolbar.json", SearchOption.AllDirectories).Where(f => File.ReadAllText(f) == "{broken"));
    }

    [Fact]
    public void ResetLayout_refuses_a_newer_file()
    {
        var session = Open("""{"version":99}""");
        var before = File.ReadAllBytes(ToolbarPath);
        Assert.Equal(ToolbarSaveStatus.Failed, session.ResetLayout().Status);
        Assert.Equal(before, File.ReadAllBytes(ToolbarPath));
    }

    [Fact]
    public void A_staged_picture_is_written_only_after_toolbar_json()
    {
        var session = Open("""{"version":3}""");
        Assert.Equal(CustomIconProblem.None, session.StagePicture("mine", Source(Real16)));
        Assert.False(Directory.Exists(Icons)); // staged in memory only
        File.Delete(Source(Real16));            // the original may vanish: the bytes are held
        Assert.Equal(ToolbarSaveStatus.Saved, session.Edit(m => AddButton(m, "mine")).Status);
        Assert.Equal(Real16, File.ReadAllBytes(Path.Combine(Icons, "mine.png")));
        Assert.Equal(["mine.png"], Directory.GetFiles(Icons).Select(Path.GetFileName));
        Assert.False(session.HasStagedPicture("mine"));
    }

    [Fact]
    public void A_refused_or_failed_save_writes_no_picture_and_drops_the_staging()
    {
        var session = Open("""{"version":3}""");
        session.StagePicture("mine", Source(Real16));
        File.WriteAllText(ToolbarPath, """{"version":3,"future":1}""");
        Assert.Equal(ToolbarSaveStatus.ChangedOnDisk, session.Edit(m => AddButton(m, "mine")).Status);
        Assert.False(Directory.Exists(Icons));
        Assert.False(session.HasStagedPicture("mine"));

        var newer = Open("""{"version":99}""");
        newer.StagePicture("mine", Source(Real16));
        Assert.Equal(ToolbarSaveStatus.Failed, newer.Edit(m => AddButton(m, "mine")).Status);
        Assert.False(Directory.Exists(Icons));
    }

    [Fact]
    public void A_picture_only_edit_respects_the_conflict_rule_and_backs_up_the_old_picture()
    {
        var session = Open("""{"version":3,"buttons":{"mine":{"text":"hi","iconFile":"mine.png"}}}""");
        Directory.CreateDirectory(Icons);
        File.WriteAllBytes(Path.Combine(Icons, "mine.png"), Real16);
        Assert.Equal(CustomIconProblem.None, session.StagePicture("mine", Source(Real16)));
        File.WriteAllText(ToolbarPath, """{"version":3,"buttons":{"mine":{"text":"hi","iconFile":"mine.png"}},"x":1}""");
        Assert.Equal(ToolbarSaveStatus.ChangedOnDisk, session.Edit(m => m).Status);

        session.Reload();
        File.Delete(Path.Combine(Icons, "mine.png"));
        File.WriteAllBytes(Path.Combine(Icons, "mine.png"), [1, 2, 3]);
        session.StagePicture("mine", Source(Real16));
        Assert.Equal(ToolbarSaveStatus.Saved, session.Edit(m => m).Status);
        Assert.Equal(Real16, File.ReadAllBytes(Path.Combine(Icons, "mine.png")));
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(_dir, "backups"), "mine.png", SearchOption.AllDirectories));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(backup));
    }

    [Fact]
    public void Staging_rejects_bad_pictures_and_ids_and_skips_a_button_the_model_does_not_name()
    {
        var session = Open("""{"version":3}""");
        Assert.Equal(CustomIconProblem.NotPng, session.StagePicture("mine", Source(Encoding.ASCII.GetBytes("<svg/>"))));
        Assert.Equal(CustomIconProblem.Unreadable, session.StagePicture("../evil", Source(Real16)));
        Assert.Equal(CustomIconProblem.Unreadable, session.StagePicture("mine", Path.Combine(_dir, "missing.png")));
        session.StagePicture("ghost", Source(Real16));
        Assert.Equal(ToolbarSaveStatus.Saved, session.Edit(m => AddButton(m, "other")).Status);
        Assert.False(Directory.Exists(Icons));
    }

    [Fact]
    public void A_picture_that_cannot_be_stored_is_reported_after_the_save()
    {
        var session = Open("""{"version":3}""");
        Directory.CreateDirectory(Path.GetDirectoryName(Icons)!);
        File.WriteAllText(Icons, "a file where the icons folder should be");
        session.StagePicture("mine", Source(Real16));
        var result = session.Edit(m => AddButton(m, "mine"));
        Assert.Equal(ToolbarSaveStatus.Saved, result.Status);
        Assert.Equal(["mine"], result.PicturesFailed);
        Assert.Equal("mine", session.Model.Buttons.Single().Id); // toolbar.json itself was saved
    }

    [Fact]
    public void Replacing_a_picture_keeps_save_sets_and_only_that_buttons_file_changes()
    {
        var session = Open("""{"version":3}""");
        session.StagePicture("b1", Source(Real16));
        session.StagePicture("b2", Source(Real16));
        Assert.True(session.Edit(m => AddButton(AddButton(m, "b1"), "b2")).Saved);
        byte[] second = [.. Real16, 0]; // a valid picture (bytes after IEND are ignored), a different file
        var previous = File.ReadAllBytes(Path.Combine(Icons, "b1.png"));
        File.WriteAllBytes(Source(second), second);
        Assert.Equal(CustomIconProblem.None, session.StagePicture("b1", Source(second)));
        Assert.True(session.Edit(m => m).Saved);
        Assert.Equal(second, File.ReadAllBytes(Path.Combine(Icons, "b1.png")));
        Assert.Equal(Real16, File.ReadAllBytes(Path.Combine(Icons, "b2.png")));
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(_dir, "backups"), "b1.png", SearchOption.AllDirectories));
        Assert.Equal(previous, File.ReadAllBytes(backup));
        Assert.Equal(["b1.png", "b2.png"], Directory.GetFiles(Icons).Select(Path.GetFileName).Order()); // no .prev or temp files
    }

    [Fact]
    public void A_refused_picture_is_not_staged_so_the_old_one_stays()
    {
        var session = Open("""{"version":3,"buttons":{"mine":{"text":"hi","iconFile":"mine.png"}}}""");
        Directory.CreateDirectory(Icons);
        File.WriteAllBytes(Path.Combine(Icons, "mine.png"), Real16);
        Assert.Equal(CustomIconProblem.NotPng, session.StagePicture("mine", Source(Encoding.ASCII.GetBytes("<svg/>"))));
        Assert.False(session.HasStagedPicture("mine"));
        Assert.Equal(ToolbarSaveStatus.Unchanged, session.Edit(m => m).Status);
        Assert.Equal(Real16, File.ReadAllBytes(Path.Combine(Icons, "mine.png")));
    }

    [Theory]
    [InlineData(@"\\server\share\a.png")]
    [InlineData("//server/share/a.png")]
    [InlineData(@"\\?\C:\a.png")]
    [InlineData(@"\a.png")]
    [InlineData("a.png")]
    [InlineData("")]
    public void Staging_refuses_network_and_relative_paths(string source)
    {
        var session = Open("""{"version":3}""");
        Assert.Equal(CustomIconProblem.Unreadable, session.StagePicture("mine", source));
        Assert.False(Directory.Exists(Icons));
    }

    private const string WithMine = """{"version":3,"buttons":{"mine":{"label":"mine","icon":"","text":"hi","iconFile":"mine.png"}},"layout":{"topLeft":["mine"]}}""";

    private string[] BackupFolders() =>
        Directory.Exists(Path.Combine(_dir, "backups")) ? Directory.GetDirectories(Path.Combine(_dir, "backups")) : [];

    // A newer toolbar.json is read as far as understood and never written: its pictures aren't either.
    [Fact]
    public void A_picture_only_edit_of_a_newer_file_writes_nothing()
    {
        var session = Open(WithMine.Replace("\"version\":3", "\"version\":4"));
        Directory.CreateDirectory(Icons);
        File.WriteAllBytes(Path.Combine(Icons, "mine.png"), [1, 2, 3]);
        Assert.Equal(CustomIconProblem.None, session.StagePicture("mine", Source(Real16)));

        Assert.Equal(ToolbarSaveStatus.Failed, session.Edit(m => m).Status);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(Icons, "mine.png")));
        Assert.Empty(BackupFolders());
    }

    // One save is one operation: toolbar.json and the pictures it replaces go to the same backup folder.
    [Fact]
    public void One_save_makes_one_backup_set_for_toolbar_json_and_pictures()
    {
        var session = Open(WithMine);
        Directory.CreateDirectory(Icons);
        File.WriteAllBytes(Path.Combine(Icons, "mine.png"), [1, 2, 3]);
        session.StagePicture("mine", Source(Real16));

        var result = session.Edit(m => ToolbarEditor.UpdateCustom(m, m.Buttons.Single(b => b.Id == "mine") with { Label = "Mine!" }));

        Assert.Equal(ToolbarSaveStatus.Saved, result.Status);
        var set = Assert.Single(BackupFolders());
        Assert.True(File.Exists(Path.Combine(set, "toolbar.json")));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(set, "icons", "mine.png")));
    }

    // Removing a button, or taking its picture away, removes the picture file too, after backing it up in the same set.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_picture_the_saved_bar_no_longer_uses_is_backed_up_and_removed(bool deleteButton)
    {
        var session = Open(WithMine);
        Directory.CreateDirectory(Icons);
        File.WriteAllBytes(Path.Combine(Icons, "mine.png"), [1, 2, 3]);

        var result = session.Edit(m => deleteButton
            ? ToolbarEditor.RemoveCustom(m, "mine")
            : ToolbarEditor.UpdateCustom(m, m.Buttons.Single(b => b.Id == "mine") with { IconFile = null }));

        Assert.Equal(ToolbarSaveStatus.Saved, result.Status);
        Assert.False(File.Exists(Path.Combine(Icons, "mine.png")));
        var set = Assert.Single(BackupFolders());
        Assert.True(File.Exists(Path.Combine(set, "toolbar.json")));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(set, "icons", "mine.png")));
    }

    [Fact]
    public void A_picture_still_used_after_a_save_stays()
    {
        var session = Open(WithMine);
        Directory.CreateDirectory(Icons);
        File.WriteAllBytes(Path.Combine(Icons, "mine.png"), [1, 2, 3]);
        Assert.Equal(ToolbarSaveStatus.Saved, session.SetShortcut("pin", "Ctrl+Q").Status);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(Icons, "mine.png")));
    }

    // Only a button's own icons\<id>.png is ever removed: nothing outside the folder, no other button's picture, old
    // backup-like files stay.
    [Fact]
    public void Removing_a_picture_touches_only_that_buttons_own_file()
    {
        var session = Open(WithMine);
        Directory.CreateDirectory(Icons);
        File.WriteAllBytes(Path.Combine(Icons, "mine.png"), [1]);
        File.WriteAllBytes(Path.Combine(Icons, "other.png"), [2]);
        File.WriteAllText(Path.Combine(Icons, "mine.png.old.bak"), "old");
        File.WriteAllBytes(Path.Combine(_dir, "outside.png"), [3]);

        Assert.Equal(ToolbarSaveStatus.Saved, session.Edit(m => ToolbarEditor.RemoveCustom(m, "mine")).Status);

        Assert.Equal(["mine.png.old.bak", "other.png"], Directory.GetFiles(Icons).Select(Path.GetFileName).Order());
        Assert.True(File.Exists(Path.Combine(_dir, "outside.png")));
    }
}
