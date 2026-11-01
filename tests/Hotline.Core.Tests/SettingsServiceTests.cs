using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private SettingsService New(out SettingsStore store)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "connections"));
        File.WriteAllText(Path.Combine(_dir, "connections", "agy.json"), """{ "type": "antigravity", "name": "Gemini (Antigravity)", "agent": "hotline" }""");
        store = new SettingsStore(_dir);
        return new SettingsService(store, store.Load(), new FileLog(Path.Combine(_dir, "h.log")));
    }

    [Fact]
    public void Update_saves_normalizes_and_raises_changed_once()
    {
        var service = New(out var store);
        var raised = 0;
        service.Changed += () => raised++;
        var window = service.Current.Window;
        service.Update(s => s.Window.FontSize = 99);
        Assert.Equal(1, raised);
        Assert.Same(window, service.Current.Window); // other components keep the same live object
        Assert.Equal(32, service.Current.Window.FontSize);
        Assert.Equal(32, store.Load().Window.FontSize);
    }

    [Fact]
    public void Reload_copies_hand_edits_into_the_live_objects_once()
    {
        var service = New(out var store);
        var window = service.Current.Window;
        var general = service.Current.General;
        var backends = service.Current.Chat.Backends;
        Assert.True(general.SupportReminder); // on for new installs: the donation note shows once
        var raised = 0;
        service.Changed += () => raised++;

        void Edit(HotlineSettings s)
        {
            s.Window.FontSize = 18;
            s.General.SupportReminder = false;
            s.General.Language = "ja";
            s.Chat.Backends[0].Model = "gemini-3.1-pro-high";
        }
        Assert.True(service.Reload(store.Load().Also(Edit)));
        Assert.Equal(18, window.FontSize);                       // same object other components hold
        Assert.False(general.SupportReminder);
        Assert.Equal("ja", general.Language);
        Assert.Equal("gemini-3.1-pro-high", backends[0].Model);  // same list instance
        Assert.Equal(1, raised);

        Assert.False(service.Reload(store.Load().Also(Edit)));
        Assert.Equal(1, raised);
        service.Update(s => s.General.SupportReminder = true);
        Assert.True(store.Load().General.SupportReminder);
    }

    [Fact]
    public void Reload_updates_connections_in_place_and_reports_changed_ones()
    {
        var service = New(out var store);
        service.Update(s => Hotline.Core.Backends.ConnectionEditor.Add(s.Chat, BackendType.Local));
        var agy = service.Current.Chat.Backends[0];
        var local = service.Current.Chat.Backends[1];
        IReadOnlyList<string>? changed = null;
        service.ConnectionsChanged += ids => changed = ids;

        var edited = store.Load();
        edited.Chat.Backends[0].ApproveAllTools = true;  // agy changed
        edited.Chat.Backends.RemoveAt(1);                 // local removed
        Assert.True(service.Reload(edited));

        Assert.Same(agy, service.Current.Chat.Backends[0]); // live instance kept
        Assert.True(agy.ApproveAllTools);
        Assert.Single(service.Current.Chat.Backends);
        Assert.Equal(["agy", local.Id], changed!.Order());
    }

    [Fact]
    public void Update_keeps_a_backup_of_a_broken_hand_edit()
    {
        var service = New(out var store);
        File.WriteAllText(store.FilePath, "{ \"window\": ");
        service.Update(s => s.Window.HideOnBlur = false);
        Assert.Contains(Directory.GetFiles(_dir, "settings.json.*.bak"), f => File.ReadAllText(f) == "{ \"window\": ");
        Assert.NotNull(store.TryRead());
    }

    [Fact]
    public void Ui_change_right_after_a_hand_edit_keeps_the_hand_edit()
    {
        var service = New(out var store);
        service.Update(s => s.Window.HideOnBlur = true);
        var edited = store.Load();
        edited.Window.FontSize = 20;            // hand edit, not yet reloaded by the watcher
        store.Save(edited);
        File.SetLastWriteTimeUtc(store.FilePath, DateTime.UtcNow.AddSeconds(5));
        service.Update(s => s.Window.AlwaysOnTop = false); // e.g. a picker change
        var onDisk = store.Load();
        Assert.Equal((20, false), (onDisk.Window.FontSize, onDisk.Window.AlwaysOnTop));
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> change) { change(value); return value; }
}
