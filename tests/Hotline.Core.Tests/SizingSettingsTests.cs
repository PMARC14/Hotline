using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class SizingSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private HotlineSettings LoadJson(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), json);
        return new SettingsStore(_dir).Load();
    }

    [Fact]
    public void Defaults()
    {
        var w = new SettingsStore(_dir).Load().Window;
        Assert.Equal((40.0, 600, 1000, 320, 70.0), (w.WidthPercent, w.MinWidth, w.MaxWidth, w.Height, w.MaxHeightPercent));
    }

    [Theory]
    [InlineData(1, 120)]
    [InlineData(2, 120)]
    [InlineData(3, 120)]
    [InlineData(1, 520)]
    public void Old_default_heights_migrate_to_320(int schema, int height)
        => Assert.Equal(320, LoadJson($$"""{ "schemaVersion": {{schema}}, "window": { "height": {{height}} } }""").Window.Height);

    [Fact]
    public void Custom_height_is_kept()
        => Assert.Equal(400, LoadJson("""{ "schemaVersion": 3, "window": { "height": 400 } }""").Window.Height);

    [Fact]
    public void Percentages_and_widths_are_clamped()
    {
        var w = LoadJson("""{ "window": { "widthPercent": 500, "maxHeightPercent": 5, "minWidth": 50, "maxWidth": 10 } }""").Window;
        Assert.Equal(90, w.WidthPercent);
        Assert.Equal(30, w.MaxHeightPercent);
        Assert.Equal(320, w.MinWidth);
        Assert.Equal(320, w.MaxWidth); // never below MinWidth
    }
}
