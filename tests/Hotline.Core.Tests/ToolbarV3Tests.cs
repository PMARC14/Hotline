using System.Text;
using System.Text.Json.Nodes;
using Hotline.Core.Settings;
using Hotline.Core.Tests.Files;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ToolbarV3Tests : TempDirTest
{
    [Theory]
    [InlineData("default")]
    [InlineData("sparse")]
    [InlineData("unknown-keys")]
    [InlineData("mixed-case")]
    [InlineData("newer-version")]
    public void Fixtures_load_without_writes_and_save_unchanged_bytes(string name)
    {
        var fs = new FakeFileSystem();
        var path = Path.Combine(_dir, "toolbar.json");
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "toolbar", "v3", name + ".json");
        var bytes = File.ReadAllBytes(fixture);
        fs.Files[path] = bytes;
        var load = ToolbarFile.Load(path, fs);
        Assert.Null(load.Error);
        Assert.Empty(fs.Writes);
        ToolbarFile.Save(path, load.Model, fileSystem: fs);
        Assert.Equal(bytes, fs.Files[path]);
        Assert.Empty(fs.Writes);
    }

    [Fact]
    public void Resetting_the_bar_keeps_shortcuts_and_unknown_shortcut_keys()
    {
        var fs = new FakeFileSystem();
        var path = Path.Combine(_dir, "toolbar.json");
        fs.Files[path] = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "toolbar", "v3", "unknown-keys.json"));
        Assert.Null(ToolbarFile.Save(path, ToolbarModel.ResetLayout(ToolbarFile.Load(path, fs).Model), fileSystem: fs));
        var shortcuts = JsonNode.Parse(fs.Files[path])!["shortcuts"]!;
        Assert.Equal("Ctrl+Shift+Y", shortcuts["futureCommand"]!.GetValue<string>());
        Assert.Equal("Ctrl+K", shortcuts["pin"]!.GetValue<string>());
        Assert.DoesNotContain("summarize", JsonNode.Parse(fs.Files[path])!["bottom"]!["left"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":2,\"items\":{\"left\":[\"settings\"]}}")]
    public void Old_files_use_defaults_and_one_warning(string json)
    {
        var model = ToolbarModel.Resolve(json);
        Assert.Equal(ToolbarModel.Defaults, model);
        Assert.Single(model.Warnings);
    }

    [Theory]
    [InlineData("{\"pin\":\"Ctrl+Q\",\"recent\":\"Ctrl+Q\"}", "recent", "pin")]
    [InlineData("{\"recent\":\"Ctrl+Q\",\"pin\":\"Ctrl+Q\"}", "recent", "pin")]
    [InlineData("{\"pin\":\"Ctrl+Q\",\"stop\":\"Ctrl+Q\"}", "stop", "pin")]
    public void Conflicts_follow_fixed_order_and_disable_losers(string shortcuts, string winner, string loser)
    {
        var model = ToolbarModel.Resolve("{\"version\":3,\"shortcuts\":" + shortcuts + "}");
        Assert.Equal("Ctrl+Q", model.EffectiveShortcuts()[winner]);
        Assert.Equal("", model.EffectiveShortcuts()[loser]);
        Assert.NotEmpty(model.Warnings);
        Assert.Equal(model, ToolbarModel.Resolve(model.ToJson()));
    }

    [Fact]
    public void Editor_save_preserves_unknown_keys_at_every_level()
    {
        var fs = new FakeFileSystem();
        var path = Path.Combine(_dir, "toolbar.json");
        var json = """{"version":3,"future":1,"shortcuts":{"future":"x"},"top":{"future":2},"bottom":{"left":["mine","pin"],"future":3},"style":{"future":4},"overrides":{"pin":{"outline":true,"future":5}},"buttons":{"mine":{"text":"hi","future":{"value":6}}}}""";
        fs.Files[path] = Encoding.UTF8.GetBytes(json);
        var model = ToolbarFile.Load(path, fs).Model;
        var edited = ToolbarEditor.SetStyle(model, "pin", new ButtonStyle(2, true));
        edited = ToolbarEditor.UpdateCustom(edited, edited.Buttons[0] with { Label = "Renamed" });
        edited = ToolbarEditor.SetStyle(edited, null, edited.Style with { Fill = false });
        edited = ToolbarEditor.SetShortcut(edited, "pin", "Ctrl+K");
        Assert.Null(ToolbarFile.Save(path, edited, fileSystem: fs));
        var root = JsonNode.Parse(fs.Files[path])!;
        foreach (var (parent, key, expected) in new[] { (root, "future", 1), (root["top"]!, "future", 2), (root["bottom"]!, "future", 3), (root["style"]!, "future", 4), (root["overrides"]!["pin"]!, "future", 5), (root["buttons"]!["mine"]!["future"]!, "value", 6) })
            Assert.Equal(expected, parent[key]!.GetValue<int>());
        Assert.Equal("x", root["shortcuts"]!["future"]!.GetValue<string>());
        Assert.Equal("Renamed", root["buttons"]!["mine"]!["label"]!.GetValue<string>());
        Assert.False(root["style"]!["fill"]!.GetValue<bool>());
        Assert.Contains(fs.Files.Keys, p => p.Contains("-save") && p.EndsWith("toolbar.json"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Clearing_an_override_retains_its_unknown_fields(bool hide)
    {
        var fs = new FakeFileSystem();
        var path = Path.Combine(_dir, "toolbar.json");
        fs.Files[path] = Encoding.UTF8.GetBytes("""{"version":3,"overrides":{"pin":{"width":2,"future":7}}}""");
        var model = ToolbarFile.Load(path, fs).Model;
        var next = hide ? ToolbarEditor.Hide(model, "pin") : ToolbarEditor.SetStyle(model, "pin", new ButtonStyle());
        Assert.Null(ToolbarFile.Save(path, next, fs));
        Assert.Equal(7, JsonNode.Parse(fs.Files[path])!["overrides"]!["pin"]!["future"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("Stop")]
    [InlineData("STOP")]
    [InlineData("Pin")]
    [InlineData("spacer")]
    public void Reserved_custom_ids_are_refused_and_generated_ids_avoid_them(string id)
    {
        var button = new CustomButton(id, "Label", "E734", null, "hi", false);
        Assert.Same(ToolbarModel.Defaults, ToolbarEditor.AddCustom(ToolbarModel.Defaults, button));
        var model = ToolbarModel.Resolve("{\"version\":3,\"buttons\":{\"" + id + "\":{\"text\":\"hi\"}}}");
        Assert.Empty(model.Buttons);
        Assert.Single(model.Warnings);
        Assert.Equal(id.ToLowerInvariant() + "-2", ToolbarEditorView.NewCustomId(model, id));
    }

    [Theory]
    [InlineData(false, 80)]
    [InlineData(true, 100)]
    public void Fill_round_trips_and_controls_row_space(bool fill, double width)
    {
        var model = ToolbarModel.Resolve("{\"version\":3,\"top\":{\"left\":[],\"center\":[],\"right\":[]},\"bottom\":{\"left\":[\"pin\",\"settings\"],\"center\":[],\"right\":[]},\"style\":{\"fill\":" + (fill ? "true" : "false") + "}}");
        Assert.Equal(fill, model.Style.Fill);
        Assert.Equal(model, ToolbarModel.Resolve(model.ToJson()));
        var row = ToolbarPlan.PlanRow(model, false, 100, 0, fill: model.Style.Fill == true);
        Assert.Equal(width, row.Used);
    }
}
