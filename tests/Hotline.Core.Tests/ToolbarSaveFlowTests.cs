using System.Text;
using System.Text.Json.Nodes;
using Hotline.Core.Tests.Files;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>Pins the toolbar.json save, conflict and reset behaviour that the ToolbarSession refactor must keep.</summary>
public sealed class ToolbarSaveFlowTests : TempDirTest
{
    private readonly FakeFileSystem _files = new();
    private string Path_ => Path.Combine(_dir, "toolbar.json");

    private void Put(string json) => _files.Files[Path_] = Encoding.UTF8.GetBytes(json);
    private string Disk => Encoding.UTF8.GetString(_files.Files[Path_]);

    [Fact]
    public void A_failed_shortcut_save_keeps_the_model_and_the_stamp_so_a_retry_works()
    {
        Put("""{"version":3}""");
        var session = new ToolbarSession(Path_, _files);
        _files.Fail = c => c.Operation == "WriteDurable";
        var failed = session.SetShortcut("pin", "Ctrl+Q");
        Assert.False(failed.ChangedOnDisk);
        Assert.NotNull(failed.Error);
        Assert.Equal("Ctrl+P", session.Load.Model.ShortcutText("pin"));
        _files.Fail = null;
        var retry = session.SetShortcut("pin", "Ctrl+Q");
        Assert.False(retry.ChangedOnDisk);
        Assert.Null(retry.Error);
        Assert.Equal("Ctrl+Q", JsonNode.Parse(Disk)!["shortcuts"]!["pin"]!.GetValue<string>());
    }

    [Fact]
    public void The_conflict_check_runs_before_the_edit_so_even_a_no_op_reports_a_changed_file()
    {
        Put("""{"version":3}""");
        var session = new ToolbarSession(Path_, _files);
        Put("""{"version":3,"shortcuts":{"pin":"Ctrl+Q"}}""");
        Assert.True(session.SetShortcut("pin", null).ChangedOnDisk);
    }

    [Fact]
    public void Rewriting_the_same_bytes_is_not_a_conflict_but_any_other_change_is()
    {
        Put("""{"version":3}""");
        var session = new ToolbarSession(Path_, _files);
        Put("""{"version":3}""");
        Assert.False(session.SetShortcut("pin", "Ctrl+Q").ChangedOnDisk);
        _files.Files.Remove(Path_);
        Assert.True(session.SetShortcut("recent", "Ctrl+K").ChangedOnDisk);
    }

    [Fact]
    public void A_newer_file_is_refused_with_an_error_and_never_written()
    {
        Put("""{"version":99,"shortcuts":{"pin":"Ctrl+Q"}}""");
        var before = _files.Files[Path_];
        var session = new ToolbarSession(Path_, _files);
        var result = session.SetShortcut("pin", "Ctrl+K");
        Assert.False(result.ChangedOnDisk);
        Assert.NotNull(result.Error);
        Assert.Empty(_files.Writes);
        Assert.Equal(before, _files.Files[Path_]);
    }

    [Fact]
    public void An_unreadable_file_loads_as_defaults_with_an_error()
    {
        Put("{broken");
        var session = new ToolbarSession(Path_, _files);
        Assert.NotNull(session.Load.Error);
        Assert.Equal(ToolbarModel.Defaults, session.Load.Model);
        Assert.Empty(_files.Writes);
    }

    [Fact]
    public void Resetting_over_an_unreadable_file_backs_it_up_and_writes_the_defaults()
    {
        Put("{broken");
        var load = ToolbarFile.Load(Path_, _files);
        Assert.Null(ToolbarFile.Save(Path_, ToolbarModel.ResetLayout(load.Error is null ? load.Model : null), _files));
        Assert.Null(ToolbarFile.Load(Path_, _files).Error);
        Assert.Contains(_files.Files, f => f.Key.Contains("backups") && f.Key.EndsWith("toolbar.json") && Encoding.UTF8.GetString(f.Value) == "{broken");
    }

    [Fact]
    public void Resetting_a_newer_file_is_refused_and_leaves_it_alone()
    {
        Put("""{"version":99}""");
        var before = _files.Files[Path_];
        var error = ToolbarFile.Save(Path_, ToolbarModel.ResetLayout(null), _files);
        Assert.NotNull(error);
        Assert.Equal(before, _files.Files[Path_]);
    }

    [Fact]
    public void Saving_an_unchanged_model_keeps_the_exact_bytes_and_writes_nothing()
    {
        Put("// note\n{ \"version\": 3,\n  \"future\": [1, 2] }\n");
        var before = _files.Files[Path_];
        var model = ToolbarFile.Load(Path_, _files).Model;
        Assert.Null(ToolbarFile.Save(Path_, model, _files));
        Assert.Empty(_files.Writes);
        Assert.Equal(before, _files.Files[Path_]);
    }

    [Fact]
    public void An_edit_keeps_unknown_keys_and_the_files_own_id_spelling()
    {
        Put("""{"version":3,"future":{"a":1},"shortcuts":{"PIN":"Ctrl+Q"},"buttons":{"MiNe":{"text":"hi"}}}""");
        var model = ToolbarFile.Load(Path_, _files).Model;
        Assert.Null(ToolbarFile.Save(Path_, ToolbarEditor.SetShortcut(model, "pin", "Ctrl+K"), _files));
        var tree = JsonNode.Parse(Disk)!;
        Assert.Equal(1, tree["future"]!["a"]!.GetValue<int>());
        Assert.Equal("Ctrl+K", tree["shortcuts"]!["PIN"]!.GetValue<string>());
        Assert.False(tree["shortcuts"]!.AsObject().ContainsKey("pin"));
        Assert.True(tree["buttons"]!.AsObject().ContainsKey("MiNe"));
    }
}
