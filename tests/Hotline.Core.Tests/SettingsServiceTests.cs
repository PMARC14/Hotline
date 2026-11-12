using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class SettingsServiceTests : TempDirTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Automatic_update_defers_when_file_is_missing_or_locked(bool locked)
    {
        var service = New(out var store);
        using var held = locked ? new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
        if (!locked) File.Delete(store.FilePath);
        Assert.NotEmpty(service.Update(s => s.Window.FontSize = 23, UpdateSource.Automatic));
        Assert.NotNull(store.SettingsError);
        Assert.Equal(23, service.Current.Window.FontSize);
        if (!locked) Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Automatic_replay_applies_once_on_retry_and_explicit_edit_keeps_queued_changes()
    {
        var service = New(out var store);
        service.Update(s => s.Window.FontSize = 20);
        var valid = File.ReadAllText(store.FilePath);
        File.WriteAllText(store.FilePath, "{");
        service.Update(s => s.Window.FontSize++, UpdateSource.Automatic);
        File.WriteAllText(store.FilePath, valid + " ");
        store.BackupOverride = _ => throw new IOException("blocked");
        service.Reload(store.TryRead()!);
        Assert.Equal(21, service.Current.Window.FontSize);
        service.Reload(store.TryRead()!);
        Assert.Equal(21, service.Current.Window.FontSize);
        store.BackupOverride = null;
        service.Update(s => s.General.Language = "ja");
        Assert.Equal(21, store.TryRead()!.Window.FontSize);
        Assert.Equal("ja", store.TryRead()!.General.Language);
        Assert.False(service.Reload(store.TryRead()!));
        Assert.Equal(21, service.Current.Window.FontSize);
    }
    [Fact]
    public void Automatic_update_waits_for_valid_file_and_preserves_hand_edit()
    {
        var service = New(out var store);
        var valid = File.ReadAllText(store.FilePath);
        File.WriteAllText(store.FilePath, "{incomplete");
        var notices = 0;
        service.Changed += () => notices++;
        Assert.NotEmpty(service.Update(s => s.Window.FontSize = 23, UpdateSource.Automatic));
        Assert.Equal("{incomplete", File.ReadAllText(store.FilePath));
        Assert.Equal(23, service.Current.Window.FontSize);
        Assert.NotEmpty(service.Update(s => s.Window.HideOnBlur = false, UpdateSource.Automatic));
        var tree = System.Text.Json.Nodes.JsonNode.Parse(valid)!;
        tree["general"]!["language"] = "ja";
        File.WriteAllText(store.FilePath, tree.ToJsonString());
        service.Reload(store.TryRead()!);
        var result = store.TryRead()!;
        Assert.Equal(23, result.Window.FontSize);
        Assert.False(result.Window.HideOnBlur);
        Assert.Equal("ja", result.General.Language);
        Assert.Equal(3, notices);
        Assert.False(service.Reload(store.TryRead()!));
        Assert.Equal(3, notices);
    }

    [Fact]
    public void Automatic_replay_retries_failed_save_without_losing_the_queue()
    {
        var service = New(out var store);
        var valid = File.ReadAllText(store.FilePath);
        File.WriteAllText(store.FilePath, "{");
        service.Update(s => s.Window.FontSize = 23, UpdateSource.Automatic);
        File.WriteAllText(store.FilePath, valid + " ");
        store.BackupOverride = _ => throw new IOException("blocked");
        service.Reload(store.TryRead()!);
        Assert.NotEqual(23, store.TryRead()!.Window.FontSize);
        Assert.Equal(23, service.Current.Window.FontSize);
        store.BackupOverride = null;
        service.Reload(store.TryRead()!);
        Assert.Equal(23, store.TryRead()!.Window.FontSize);
    }
    [Fact]
    public void Reload_preserves_memory_after_partial_save_failure_and_reset_forces_reload()
    {
        var service = New(out var store);
        File.AppendAllText(store.FilePath, " "); // hand edit requires a backup
        store.BackupOverride = path => { if (path == store.FilePath) throw new IOException("blocked"); };
        Assert.NotEmpty(service.Update(s => { s.Window.FontSize = 24; s.Chat.Backends[0].Model = "new"; }));
        var notifications = 0;
        service.Changed += () => notifications++;
        Assert.False(service.Reload(store.TryRead()!));
        Assert.Equal(24, service.Current.Window.FontSize);
        Assert.Equal("new", store.TryRead()!.Chat.Backends[0].Model);
        Assert.Equal(0, notifications);
        store.BackupOverride = null;
        Assert.Empty(service.Reset(false).Failed);
        Assert.Equal(new HotlineSettings().Window.FontSize, service.Current.Window.FontSize);
    }
    [Fact]
    public void Reload_case_only_id_edit_preserves_profile_instance()
    {
        var service = New(out var store);
        var profile = service.Current.Chat.Backends[0];
        var edited = store.Load();
        edited.Chat.Backends[0].Id = profile.Id.ToUpperInvariant();
        edited.Chat.Backends[0].Model = "changed";
        service.Reload(edited);
        Assert.Same(profile, service.Current.Chat.Backends[0]);
        Assert.Equal("changed", profile.Model);
    }

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
