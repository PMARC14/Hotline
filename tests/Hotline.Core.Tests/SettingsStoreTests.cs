using Hotline.Core.Activation;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private SettingsStore New() => new(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Missing_file_returns_defaults_and_writes_file()
    {
        var store = New();
        var s = store.Load();
        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
        Assert.Equal(640, s.Window.Width);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = New();
        var s = store.Load();
        s.Activation.Hold = KeyAction.RegionSelect;
        s.Activation.FallbackHotkey = "Ctrl+Alt+H";
        s.Window.Layout = PopupLayout.SidePanel;
        store.Save(s);

        var loaded = New().Load();
        Assert.Equal(KeyAction.RegionSelect, loaded.Activation.Hold);
        Assert.Equal("Ctrl+Alt+H", loaded.Activation.FallbackHotkey);
        Assert.Equal(PopupLayout.SidePanel, loaded.Window.Layout);
    }

    [Fact]
    public void File_uses_readable_enum_names()
    {
        var store = New();
        store.Load();
        Assert.Contains("\"togglePopup\"", File.ReadAllText(store.FilePath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_properties_keep_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), """{ "window": { "width": 800 } }""");
        var s = New().Load();
        Assert.Equal(800, s.Window.Width);
        Assert.Equal(520, s.Window.Height);
        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
    }

    [Fact]
    public void Null_sections_are_replaced_with_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), """{ "activation": null, "window": null }""");
        var s = New().Load();
        Assert.NotNull(s.Activation);
        Assert.NotNull(s.Window);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "activation": { "tap": "Teleport" } }""")]
    [InlineData("null")]
    public void Corrupt_file_is_backed_up_and_defaults_used(string content)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, SettingsStore.FileName);
        File.WriteAllText(path, content);

        var s = New().Load();

        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
        Assert.Equal(content, File.ReadAllText(path + ".bad"));
    }

    [Theory]
    [InlineData(10, 320)]
    [InlineData(99999, 4000)]
    public void Window_size_is_clamped(int width, int expected)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), $$"""{ "window": { "width": {{width}} } }""");
        Assert.Equal(expected, New().Load().Window.Width);
    }
}
