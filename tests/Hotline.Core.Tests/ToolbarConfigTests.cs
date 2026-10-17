using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ToolbarConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
    private string PathFor => System.IO.Path.Combine(_dir, "toolbar.json");

    [Fact]
    public void Missing_file_writes_the_defaults()
    {
        var items = ToolbarConfig.Load(PathFor).Items;
        Assert.Equal(ToolbarConfig.Defaults, items);
        Assert.Contains("\"captureRegion\"", File.ReadAllText(PathFor)); // listed as an available item
    }

    [Fact]
    public void Order_is_kept_unknown_and_duplicate_items_are_dropped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor, """{ "items": ["settings", "model", "bogus", "model", "captureRegion", "spacer", "spacer"] } // comment ok""");
        Assert.Equal(["settings", "model", "captureRegion", "spacer", "spacer"], ToolbarConfig.Load(PathFor).Items);
    }

    [Fact]
    public void Broken_or_empty_file_uses_defaults_without_overwriting()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor, "{ nope");
        var config = ToolbarConfig.Load(PathFor);
        Assert.Equal(ToolbarConfig.Defaults, config.Items);
        Assert.NotNull(config.Error);
        Assert.Equal("{ nope", File.ReadAllText(PathFor));
        File.WriteAllText(PathFor, """{ "items": [] }""");
        Assert.Equal(ToolbarConfig.Defaults, ToolbarConfig.Load(PathFor).Items);
    }

    [Fact]
    public void Region_capture_is_on_the_bar_by_default() =>
        Assert.Equal(ToolbarConfig.Defaults.ToList().IndexOf("captureScreen") + 1, ToolbarConfig.Defaults.ToList().IndexOf("captureRegion"));

    [Fact]
    public void Save_round_trips_and_backs_up_the_previous_file()
    {
        _ = ToolbarConfig.Load(PathFor); // writes the defaults
        ToolbarConfig.Save(PathFor, ["settings", "spacer", "model"]);
        Assert.Equal(["settings", "spacer", "model"], ToolbarConfig.Load(PathFor).Items);
        Assert.True(File.Exists(PathFor + ".bak"));
        Assert.Contains("captureWindow", File.ReadAllText(PathFor + ".bak"));
    }

    [Fact]
    public void Showing_an_item_puts_it_near_its_default_place()
    {
        var items = ToolbarConfig.Show(["pin", "captureWindow", "spacer", "model", "settings"], "captureScreen");
        Assert.Equal(["pin", "captureWindow", "captureScreen", "spacer", "model", "settings"], items);
        Assert.Equal(["settings", "pin"], ToolbarConfig.Show(["settings"], "pin").Reverse()); // nothing before it: goes first
        Assert.Equal(["pin"], ToolbarConfig.Show(["pin"], "pin")); // already shown
    }

    [Fact]
    public void Hide_and_move_edit_the_list()
    {
        Assert.Equal(["pin", "settings"], ToolbarConfig.Hide(["pin", "model", "settings"], "model"));
        Assert.Equal(["model", "pin", "settings"], ToolbarConfig.Move(["pin", "model", "settings"], 1, -1));
        Assert.Equal(["pin", "model", "settings"], ToolbarConfig.Move(["pin", "model", "settings"], 0, -1)); // at the edge: unchanged
    }

    [Fact]
    public void Top_row_loads_and_an_item_in_both_rows_stays_in_the_bottom_one()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor, """{ "top": ["model", "pin", "bogus"], "items": ["pin", "settings"] }""");
        var config = ToolbarConfig.Load(PathFor);
        Assert.Equal(["model"], config.Top);
        Assert.Equal(["pin", "settings"], config.Items);
    }

    [Fact]
    public void Old_files_have_no_top_row_and_everything_may_move_to_the_top()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor, """{ "items": ["pin"] }""");
        Assert.Empty(ToolbarConfig.Load(PathFor).Top);
        File.WriteAllText(PathFor, """{ "top": ["pin"], "items": [] }""");
        var config = ToolbarConfig.Load(PathFor);
        Assert.Equal(["pin"], config.Top);
        Assert.Empty(config.Items);
    }

    [Fact]
    public void Save_writes_both_rows_and_items_move_between_them()
    {
        ToolbarConfig.Save(PathFor, ["pin", "settings"], top: ["model"]);
        var config = ToolbarConfig.Load(PathFor);
        Assert.Equal(["model"], config.Top);
        Assert.Equal(["pin", "settings"], config.Items);
        var (top, items) = ToolbarConfig.ToRow(config.Top, config.Items, index: 0, fromTop: false) /* pin */;
        Assert.Equal(["model", "pin"], top);
        Assert.Equal(["settings"], items);
        (top, items) = ToolbarConfig.ToRow(top, items, index: 0, fromTop: true) /* model */;
        Assert.Equal(["pin"], top);
        Assert.Equal(["settings", "model"], items);
    }
}
