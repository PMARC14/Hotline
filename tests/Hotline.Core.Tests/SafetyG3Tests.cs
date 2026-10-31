using Hotline.Core.Backends;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Data safety of settings.json and the connection files (review round G3): backups, identity, races, reset, locked files.</summary>
public sealed class SafetyG3Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

    private string Conn(string file) => Path.Combine(_dir, "connections", file);
    private string Main => Path.Combine(_dir, SettingsStore.FileName);

    private void Put(string file, string text)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "connections"));
        File.WriteAllText(Conn(file), text);
    }

    private SettingsService Service(out SettingsStore store)
    {
        store = new SettingsStore(_dir);
        return new SettingsService(store, store.Load(), new FileLog(Path.Combine(_dir, "h.log")));
    }

    private const string HandEdited = """{"id":"a","type":"local","name":"A"}""";

    // ---- item 1: a backup exists before any replacement --------------------------------------------

    [Fact]
    public void A_valid_hand_edited_connection_file_is_copied_to_a_bak_before_it_is_rewritten()
    {
        Put("a.json", HandEdited);
        var service = Service(out _);
        service.Update(s => s.Chat.Backends[0].Model = "m1");
        Assert.Contains("m1", File.ReadAllText(Conn("a.json")));
        Assert.Equal(HandEdited, File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_dir, "connections"), "a.json.*.bak"))));
    }

    [Fact]
    public void A_hand_edited_valid_settings_file_is_copied_before_the_next_save_and_our_own_output_is_not()
    {
        var service = Service(out _);
        service.Update(s => s.Window.FontSize = 15); // our own output replaces our own output: no copies
        service.Update(s => s.Window.FontSize = 16);
        Assert.Empty(Directory.GetFiles(_dir, "settings.json.*.bak"));
        var edited = "// my notes\n" + File.ReadAllText(Main).Replace("16", "17");
        File.WriteAllText(Main, edited);
        service.Update(s => s.Window.FontSize = 18);
        Assert.Equal(edited, File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "settings.json.*.bak"))));
    }

    [Fact]
    public void A_failed_backup_skips_the_write_and_reports_a_problem()
    {
        Put("a.json", HandEdited);
        var service = Service(out var store);
        store.BackupOverride = _ => throw new IOException("disk full"); // the copy can't be made
        service.Update(s => s.Chat.Backends[0].Model = "m1");
        Assert.Equal(HandEdited, File.ReadAllText(Conn("a.json")));
        Assert.Contains(store.WriteProblems, p => p.Contains("a.json"));
        Assert.Contains(service.Problems, p => p.Contains("a.json"));
    }

    [Fact]
    public void Backups_are_never_overwritten_and_only_the_newest_ten_are_kept()
    {
        Put("a.json", HandEdited);
        var service = Service(out var store);
        store.Clock = () => new DateTime(2026, 10, 31, 12, 0, 0); // the same second every time
        for (var i = 0; i < 14; i++)
        {
            File.WriteAllText(Conn("a.json"), $$"""{"id":"a","type":"local","name":"v{{i}}"}"""); // a hand edit between saves
            service.Update(s => s.Chat.Backends[0].Model = "m" + i);
        }
        var copies = Directory.GetFiles(Path.Combine(_dir, "connections"), "a.json.*.bak");
        Assert.Equal(10, copies.Length);
        var texts = copies.Select(File.ReadAllText).ToList();
        Assert.Contains(texts, t => t.Contains("\"v13\""));   // the newest is kept
        Assert.DoesNotContain(texts, t => t.Contains("\"v0\"")); // the oldest went
    }

    // ---- item 2: a connection file added just before a save survives -------------------------------

    [Fact]
    public void A_connection_file_added_externally_just_before_a_save_is_not_deleted()
    {
        Put("a.json", HandEdited);
        var service = Service(out _);
        Put("late.json", """{ "type": "local", "name": "Late" }"""); // the watcher hasn't reloaded it yet
        service.Update(s => s.Window.FontSize = 20);
        Assert.True(File.Exists(Conn("late.json")));
        Assert.Contains(service.Current.Chat.Backends, b => b.Id == "late");
    }

    [Fact]
    public void Only_a_connection_the_user_removed_loses_its_file()
    {
        Put("a.json", HandEdited);
        Put("b.json", """{ "id":"b","type":"local","name":"B" }""");
        var service = Service(out _);
        service.Update(s => ConnectionEditor.Remove(s.Chat, "b"));
        Assert.True(File.Exists(Conn("a.json")));
        Assert.False(File.Exists(Conn("b.json")));
    }

    [Fact]
    public void A_save_that_could_not_be_made_is_reported_and_the_next_update_keeps_memory_instead_of_re_reading_the_disk()
    {
        Put("a.json", HandEdited);
        var service = Service(out var store);
        var raised = new List<string>();
        service.SaveFailed += raised.AddRange;
        store.BackupOverride = _ => throw new IOException("disk full");
        var problems = service.Update(s => s.Chat.Backends[0].Name = "Edited");
        Assert.NotEmpty(problems);
        Assert.Equal(problems, raised);
        Assert.Equal(HandEdited, File.ReadAllText(Conn("a.json"))); // left alone
        store.BackupOverride = null;
        service.Update(s => s.Window.FontSize = 20); // the disk still has the old name: memory must not be replaced by it
        Assert.Equal("Edited", service.Current.Chat.Backends[0].Name);
        Assert.Contains("Edited", File.ReadAllText(Conn("a.json"))); // and now it is written (with its copy)
    }

    // ---- item 3: a connection keeps its own file ---------------------------------------------------

    [Fact]
    public void A_file_named_differently_from_its_id_is_saved_back_to_itself_and_not_read_twice()
    {
        Put("mine.json", """{"id":"local","type":"local","name":"Mine"}""");
        var service = Service(out var store);
        service.Update(s => s.Chat.Backends[0].Model = "x");
        Assert.Contains("\"x\"", File.ReadAllText(Conn("mine.json")));
        Assert.False(File.Exists(Conn("local.json")));
        Assert.Empty(service.Problems); // no "duplicate id" notice from a second copy
        Assert.Single(new SettingsStore(_dir).Load().Chat.Backends);
        Assert.Equal(Path.GetFullPath(Conn("mine.json")), store.SourcePath("local"));
    }

    [Fact]
    public void Removing_a_connection_deletes_its_own_source_file()
    {
        Put("mine.json", """{"id":"local","type":"local","name":"Mine"}""");
        var service = Service(out _);
        service.Update(s => ConnectionEditor.Remove(s.Chat, "local"));
        Assert.False(File.Exists(Conn("mine.json")));
    }

    [Fact]
    public void Two_connections_that_would_share_a_file_are_rejected_with_a_problem()
    {
        var service = Service(out var store);
        service.Update(s => s.Chat.Backends.Add(new BackendProfile { Id = "work a", Type = BackendType.Local, Name = "One" }));
        service.Update(s => s.Chat.Backends.Add(new BackendProfile { Id = "work-a", Type = BackendType.Local, Name = "Two" }));
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "connections"), "*.json"));
        Assert.Contains("One", File.ReadAllText(Conn("work-a.json")));
        Assert.Contains(store.WriteProblems, p => p.Contains("work-a"));
    }

    [Fact]
    public void A_new_connection_cannot_take_the_file_of_another_connection()
    {
        Put("mine.json", """{"id":"local","type":"local","name":"Mine"}""");
        var service = Service(out var store);
        service.Update(s => s.Chat.Backends.Add(new BackendProfile { Id = "mine", Type = BackendType.Local, Name = "New" }));
        Assert.Contains("Mine", File.ReadAllText(Conn("mine.json")));
        Assert.NotEmpty(store.WriteProblems);
    }

    // ---- item 6: Reset with a locked file ----------------------------------------------------------

    [Fact]
    public void Reset_with_one_locked_connection_file_resets_the_rest_and_returns_the_removed_ids()
    {
        Put("a.json", """{"id":"a","type":"local","name":"A"}""");
        Put("b.json", """{"id":"b","type":"local","name":"B"}""");
        var service = Service(out _);
        service.Update(s => s.Window.FontSize = 20);
        using (new FileStream(Conn("a.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = service.Reset(everything: true); // must not throw
            Assert.Contains("b", result.Removed);
            Assert.DoesNotContain("a", result.Removed); // its file (and so its connection and key) is still there
            Assert.Contains("a.json", result.Failed);
            Assert.False(File.Exists(Conn("b.json")));
            Assert.True(File.Exists(Path.Combine(result.Backup, "connections", "b.json")));
        }
        Assert.Null(new SettingsStore(_dir).Load().Window.FontSize); // settings.json was reset too
    }

    // ---- item 9: window.layout was written and read nowhere ----------------------------------------

    [Fact]
    public void An_old_file_with_window_layout_loads_without_a_problem_and_the_next_save_drops_it()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Main, """{ "window": { "layout": "sidePanel", "width": 700 } }""");
        var store = new SettingsStore(_dir);
        var s = store.Load();
        Assert.Equal(700, s.Window.Width);
        Assert.Empty(store.SettingsProblems);
        store.Save(s);
        Assert.DoesNotContain("layout", File.ReadAllText(Main));
    }

    // ---- item 7: a locked settings.json at start-up ------------------------------------------------

    [Fact]
    public void A_locked_settings_file_at_startup_still_loads_the_connections_and_reports_the_error()
    {
        Put("a.json", """{"id":"a","type":"local","name":"A"}""");
        new SettingsStore(_dir).Save(new HotlineSettings());
        var store = new SettingsStore(_dir);
        HotlineSettings s;
        using (new FileStream(Main, FileMode.Open, FileAccess.Read, FileShare.None))
            s = store.Load();
        Assert.Equal("a", Assert.Single(s.Chat.Backends).Id);
        Assert.NotNull(store.SettingsError);
        Assert.Equal(HotlineSettings.CurrentSchemaVersion, s.SchemaVersion);
    }
}
