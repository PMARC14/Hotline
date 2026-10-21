using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>A hand-edited settings.json with null list elements must not crash start-up or a live reload.</summary>
public sealed class SettingsHandEditTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private SettingsStore Write(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), json);
        return new SettingsStore(_dir);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 14, "window": { "minWidth": 800 }, "chat": { "order": [null] } }""")]
    [InlineData("""{ "schemaVersion": 14, "window": { "minWidth": 800 }, "chat": { "order": [null, "", "  ", "a"] } }""")]
    [InlineData("""{ "schemaVersion": 14, "window": { "minWidth": 800 }, "chat": { "backends": [null] } }""")]
    [InlineData("""{ "schemaVersion": 14, "window": { "minWidth": 800 }, "chat": { "backends": [null, { "id": null }, { "id": "x", "name": "X" }] } }""")]
    public void Null_list_elements_do_not_crash_load_or_try_read(string json)
    {
        var store = Write(json);
        var loaded = store.Load();
        Assert.Equal(800, loaded.Window.MinWidth);
        var again = store.TryRead();
        Assert.NotNull(again);
        Assert.Equal(800, again.Window.MinWidth);
    }
}
