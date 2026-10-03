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
}
