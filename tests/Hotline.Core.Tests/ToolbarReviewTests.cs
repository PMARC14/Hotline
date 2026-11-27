using System.Text;
using System.Text.Json.Nodes;
using Hotline.Core.Settings;
using Hotline.Core.Tests.Files;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ToolbarReviewTests : TempDirTest
{
    [Fact]
    public void Planning_and_editor_actions_match_mixed_case_custom_ids()
    {
        var model = Put("""{"version":3,"buttons":{"MiNe":{"text":"hi"}}}""");
        var placed = Enum.GetValues<Quadrant>().ToDictionary(q => q, q => (IReadOnlyList<string>)(q == Quadrant.BottomLeft ? new[] { "mine", "MINE" } : []));
        var input = model with { Placed = placed, Menu = [] };
        Assert.Empty(ToolbarPlan.Unknown(input));
        Assert.Equal(new[] { "MiNe" }, ToolbarPlan.Row(input, false).Left);
        var shown = ToolbarEditor.Show(model, "MINE", Quadrant.TopLeft);
        Assert.Contains("MiNe", shown.Items(Quadrant.TopLeft));
        var hidden = ToolbarEditor.Hide(shown, "mine");
        Assert.DoesNotContain("MiNe", hidden.Items(Quadrant.TopLeft));
        Assert.Empty(ToolbarEditor.RemoveCustom(hidden, "MINE").Buttons);
    }

    [Fact]
    public void Shortcut_cards_share_latest_model_and_stamp_but_refuse_outside_edits()
    {
        Put("""{"version":3}""");
        var page = new ToolbarSession(Path_, _files);
        var firstCard = page;
        var secondCard = page;
        var first = firstCard.SetShortcut("PIN", "Ctrl+Q");
        Assert.False(first.ChangedOnDisk);
        Assert.Null(first.Error);
        var second = secondCard.SetShortcut("recent", "Ctrl+K");
        Assert.False(second.ChangedOnDisk);
        Assert.Null(second.Error);
        Assert.Equal("Ctrl+Q", secondCard.Load.Model.ShortcutText("pin"));
        Assert.Equal("Ctrl+K", firstCard.Load.Model.ShortcutText("recent"));
        var original = _files.Files[Path_];
        _files.Files[Path_] = Encoding.UTF8.GetBytes("// outside edit\n" + Encoding.UTF8.GetString(original));
        var changed = page.SetShortcut("pin", "Ctrl+J");
        Assert.True(changed.ChangedOnDisk);
        Assert.Equal("Ctrl+Q", page.Load.Model.ShortcutText("pin"));
        Assert.StartsWith("// outside edit", Encoding.UTF8.GetString(_files.Files[Path_]));
    }

    [Fact]
    public void Mixed_case_custom_ids_match_everywhere_and_first_definition_wins()
    {
        var model = Put("""{"version":3,"top":{"left":[],"center":[],"right":[]},"bottom":{"left":["mINE","more"],"center":[],"right":[]},"menu":["OtHeR"],"overrides":{"MINE":{"width":2},"PROVIDER":{"span":6}},"buttons":{"MiNe":{"text":"first","iconFile":"mine.PNG","shortcut":"Ctrl+Alt+1"},"mine":{"text":"second"},"oThEr":{"text":"other"}}}""");
        Assert.Equal(new[] { "MiNe", "oThEr" }, model.Buttons.Select(b => b.Id));
        Assert.Single(model.Warnings);
        Assert.Equal(new[] { "MiNe", "more" }, model.Items(Quadrant.BottomLeft));
        Assert.Equal(new[] { "oThEr" }, model.Menu);
        Assert.Equal(2, model.EffectiveStyle("mInE").Width);
        Assert.Equal(6, model.EffectiveSpan("provider"));
        var next = ToolbarEditor.SetStyle(model, "mine", new ButtonStyle(3));
        next = ToolbarEditor.UpdateCustom(next, next.Buttons[0] with { Id = "MINE", Label = "Changed" });
        next = ToolbarEditor.SetPickerSpan(next, "PROVIDER", 7);
        var tree = Save(next);
        Assert.Equal(new[] { "MiNe", "mine", "oThEr" }, tree["buttons"]!.AsObject().Select(p => p.Key));
        Assert.Equal("Changed", tree["buttons"]!["MiNe"]!["label"]!.GetValue<string>());
        Assert.Equal("second", tree["buttons"]!["mine"]!["text"]!.GetValue<string>());
        Assert.Equal(3, tree["overrides"]!["MINE"]!["width"]!.GetValue<int>());
        Assert.Equal(7, tree["overrides"]!["PROVIDER"]!["span"]!.GetValue<int>());
        Assert.False(tree["overrides"]!.AsObject().ContainsKey("provider"));
        Assert.Single(ToolbarFile.Load(Path_, _files).Model.Warnings);
        Assert.Equal("mi-ne", ToolbarEditorView.NewCustomId(ToolbarModel.Defaults, "Mi Ne"));
    }

    [Theory]
    [InlineData("MiNe.PNG", "mine")]
    [InlineData("mine.png", "MiNe")]
    [InlineData("MINE.pNg", "MiNe")]
    public void Icon_names_match_ids_without_case(string file, string id)
    {
        Assert.True(CustomIcons.IsFileName(file, id));
        Assert.Equal(Path.Combine(_dir, file), CustomIcons.IconPath(_dir, file));
    }

    private readonly FakeFileSystem _files = new();
    private string Path_ => Path.Combine(_dir, "toolbar.json");
    private ToolbarModel Put(string json)
    {
        _files.Files[Path_] = Encoding.UTF8.GetBytes(json);
        return ToolbarFile.Load(Path_, _files).Model;
    }
    private JsonObject Save(ToolbarModel model)
    {
        Assert.Null(ToolbarFile.Save(Path_, model, _files));
        return JsonNode.Parse(_files.Files[Path_])!.AsObject();
    }

    [Theory]
    [InlineData("button")]
    [InlineData("shortcut")]
    [InlineData("override")]
    public void Removing_entries_leaves_no_null_stubs_or_reload_warnings(string edit)
    {
        var model = Put("""{"version":3,"buttons":{"mine":{"text":"hi"},"other":{"text":"ok","future":7}},"shortcuts":{"pin":"Ctrl+Q"},"overrides":{"pin":{"width":2},"settings":{"outline":true,"future":8}}}""");
        var next = edit switch
        {
            "button" => ToolbarEditor.RemoveCustom(model, "mine"),
            "shortcut" => ToolbarEditor.SetShortcut(model, "pin", null),
            _ => ToolbarEditor.SetStyle(model, "pin", new ButtonStyle()),
        };
        var tree = Save(next);
        var (map, id) = edit switch { "button" => ("buttons", "mine"), "shortcut" => ("shortcuts", "pin"), _ => ("overrides", "pin") };
        Assert.False(tree[map]!.AsObject().ContainsKey(id));
        Assert.Equal(7, tree["buttons"]!["other"]!["future"]!.GetValue<int>());
        Assert.Equal(8, tree["overrides"]!["settings"]!["future"]!.GetValue<int>());
        Assert.Empty(ToolbarFile.Load(Path_, _files).Model.Warnings);
    }

    [Fact]
    public void Clearing_override_removes_known_fields_and_retains_extensions_without_warning()
    {
        var model = Put("""{"version":3,"overrides":{"pin":{"width":2,"outline":true,"future":7}}}""");
        var tree = Save(ToolbarEditor.SetStyle(model, "pin", new ButtonStyle()));
        Assert.Equal("""{"future":7}""", tree["overrides"]!["pin"]!.ToJsonString());
        Assert.Empty(ToolbarFile.Load(Path_, _files).Model.Warnings);
    }

    [Theory]
    [InlineData("pin")]
    [InlineData("mine")]
    public void Hidden_overrides_survive_load_hide_show_menu_and_save(string id)
    {
        var model = Put("""{"version":3,"bottom":{"left":[],"center":[],"right":[]},"buttons":{"mine":{"text":"hi"}},"overrides":{"pin":{"width":2},"mine":{"width":3}}}""");
        Assert.Empty(model.Warnings);
        var style = model.Overrides[id];
        model = ToolbarEditor.Show(model, id.ToUpperInvariant(), Quadrant.BottomLeft);
        model = ToolbarEditor.Hide(model, id.ToUpperInvariant());
        Assert.Equal(style, model.Overrides[id]);
        Save(model);
        model = ToolbarFile.Load(Path_, _files).Model;
        Assert.Empty(model.Warnings);
        model = ToolbarEditor.MoveToMenu(model, id.ToUpperInvariant(), 0);
        Save(model);
        Assert.Equal(style, ToolbarFile.Load(Path_, _files).Model.Overrides[id]);
    }

    [Theory]
    [InlineData("PIN")]
    [InlineData("pin")]
    public void Id_edits_preserve_original_disk_key_case_and_extensions(string id)
    {
        var model = Put("""{"version":3,"shortcuts":{"PIN":"Ctrl+Q"},"overrides":{"PIN":{"width":2,"future":7},"MINE":{"outline":true,"future":8}},"buttons":{"mine":{"text":"hi","future":9}}}""");
        var next = ToolbarEditor.SetShortcut(model, id, "Ctrl+K");
        Assert.Equal("Ctrl+K", next.ShortcutText("pin"));
        next = ToolbarEditor.SetStyle(next, id, new ButtonStyle(3));
        next = ToolbarEditor.SetStyle(next, "MINE", new ButtonStyle(Filled: true));
        next = ToolbarEditor.UpdateCustom(next, next.Buttons[0] with { Id = "MINE", Label = "Changed" });
        var tree = Save(next);
        Assert.Equal(new[] { "PIN" }, tree["shortcuts"]!.AsObject().Select(p => p.Key));
        Assert.Equal("Ctrl+K", tree["shortcuts"]!["PIN"]!.GetValue<string>());
        Assert.Equal(new[] { "PIN", "MINE" }, tree["overrides"]!.AsObject().Select(p => p.Key));
        Assert.Equal(3, tree["overrides"]!["PIN"]!["width"]!.GetValue<int>());
        Assert.Equal(new[] { "mine" }, tree["buttons"]!.AsObject().Select(p => p.Key));
        Assert.Equal("Changed", tree["buttons"]!["mine"]!["label"]!.GetValue<string>());
        Assert.Equal(9, tree["buttons"]!["mine"]!["future"]!.GetValue<int>());
        Assert.Empty(ToolbarFile.Load(Path_, _files).Model.Warnings);
    }

    [Theory]
    [InlineData("Ctrl+A")]
    [InlineData("Ctrl+C")]
    [InlineData("Ctrl+V")]
    [InlineData("Ctrl+X")]
    [InlineData("Ctrl+Y")]
    [InlineData("Ctrl+Z")]
    [InlineData("Ctrl+Tab")]
    [InlineData("Ctrl+Backspace")]
    [InlineData("Ctrl+Shift+Z")]
    [InlineData("Ctrl+Shift+Tab")]
    public void Stop_refuses_editing_keys_and_defaults_with_warning(string combo)
    {
        var model = Put("{\"version\":3,\"shortcuts\":{\"stop\":\"" + combo + "\"}}");
        Assert.Equal("Esc", model.EffectiveShortcuts()["stop"]);
        Assert.Single(model.Warnings);
        Assert.False(ChatShortcuts.CanBind(model, "stop", combo));
    }

    [Fact]
    public void Unknown_keys_fixture_survives_edits_and_conflict_loser_stays_requested_on_disk()
    {
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "toolbar", "v3", "unknown-keys.json"));
        var model = Put(fixture);
        model = ToolbarEditor.SetShortcut(model, "recent", "Ctrl+K"); // pin asks for Ctrl+K too; recent wins.
        model = ToolbarEditor.UpdateCustom(model, model.Buttons[0] with { Label = "Changed" });
        var tree = Save(model);
        var original = JsonNode.Parse(fixture)!;
        foreach (var parent in new[] { "top", "bottom", "style" })
            foreach (var (key, value) in original[parent]!.AsObject().Where(p => p.Key.StartsWith("future")))
                Assert.True(JsonNode.DeepEquals(value, tree[parent]![key]));
        Assert.Equal("kept", tree["futureTop"]!.GetValue<string>());
        Assert.Equal("ok", tree["overrides"]!["provider"]!["futureOverrideProp"]!.GetValue<string>());
        Assert.True(tree["overrides"]!["pin"]!["futureBtnOverride"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(original["buttons"]!["summarize"]!["futureButtonProp"], tree["buttons"]!["summarize"]!["futureButtonProp"]));
        Assert.Equal("Ctrl+K", tree["shortcuts"]!["pin"]!.GetValue<string>());
        var again = ToolbarFile.Load(Path_, _files).Model;
        Assert.Equal("Ctrl+K", again.EffectiveShortcuts()["recent"]);
        Assert.Equal("", again.EffectiveShortcuts()["pin"]);
        Assert.Contains(again.Warnings, w => w.Contains("pin"));
    }
}
