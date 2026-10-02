using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private SettingsService New(out SettingsStore store)
    {
        store = new SettingsStore(_dir);
        return new SettingsService(store, store.Load(), new FileLog(Path.Combine(_dir, "h.log")));
    }

    [Fact]
    public void Update_saves_normalizes_and_raises_changed_once()
    {
        var service = New(out var store);
        var raised = 0;
        service.Changed += () => raised++;
        service.Update(s => s.Window.FontSize = 99);
        Assert.Equal(1, raised);
        Assert.Equal(32, service.Current.Window.FontSize);
        Assert.Equal(32, store.Load().Window.FontSize);
    }

    [Fact]
    public void Current_is_the_same_instance_other_components_hold()
    {
        var service = New(out _);
        var window = service.Current.Window;
        service.Update(s => s.Window.HideOnBlur = false);
        Assert.False(window.HideOnBlur);
    }


    [Fact]
    public void Reload_copies_hand_edits_into_the_live_objects_once()
    {
        var service = New(out var store);
        var window = service.Current.Window;
        var backends = service.Current.Chat.Backends;
        var raised = 0;
        service.Changed += () => raised++;

        var edited = store.Load();
        edited.Window.FontSize = 18;
        edited.Chat.Backends[0].Model = "gemini-3.1-pro-high";
        Assert.True(service.Reload(edited));
        Assert.Equal(18, window.FontSize);                       // same object other components hold
        Assert.Equal("gemini-3.1-pro-high", backends[0].Model);  // same list instance
        Assert.Equal(1, raised);

        Assert.False(service.Reload(store.Load().Also(s => { s.Window.FontSize = 18; s.Chat.Backends[0].Model = "gemini-3.1-pro-high"; })));
        Assert.Equal(1, raised);
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> change) { change(value); return value; }
}
