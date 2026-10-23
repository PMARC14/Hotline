using Hotline.Core.Activation;
using Hotline.Core.Backends;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private SettingsStore New() => new(_dir);
    private string SettingsPath => Path.Combine(_dir, SettingsStore.FileName);
    private string Conn(string id) => Path.Combine(_dir, SettingsStore.ConnectionsFolder, id + ".json");
    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); // a test leaves a read-only file
        Directory.Delete(_dir, recursive: true);
    }

    private void WriteSettings(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, json);
    }

    [Fact]
    public void Missing_file_returns_defaults_and_writes_file()
    {
        var store = New();
        var s = store.Load();
        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
        Assert.Equal((25.0, 460, 1000, 0, 60.0), (s.Window.WidthPercent, s.Window.MinWidth, s.Window.MaxWidth, s.Window.Height, s.Window.MaxHeightPercent));
        Assert.Equal(BackdropKind.AcrylicThin, s.Window.Backdrop);
        Assert.InRange(s.Window.TintOpacity, 0.0, 0.3);
        Assert.False(s.Diagnostics.VerboseLogging);
        Assert.Equal(0.8, s.Window.Anchor.Y);
        Assert.Equal(14, s.SchemaVersion);
        Assert.Equal(14, HotlineSettings.CurrentSchemaVersion);
        Assert.True(File.Exists(store.FilePath));
        Assert.False(File.Exists(store.FilePath + ".old"));
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
        WriteSettings("""{ "window": { "minWidth": 800 } }""");
        var s = New().Load();
        Assert.Equal(800, s.Window.MinWidth);
        Assert.Equal(0, s.Window.Height);
        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
    }

    [Fact]
    public void Null_sections_are_replaced_with_defaults()
    {
        WriteSettings("""{ "activation": null, "window": null, "diagnostics": null }""");
        var s = New().Load();
        Assert.NotNull(s.Activation);
        Assert.NotNull(s.Window);
        Assert.NotNull(s.Diagnostics);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    public void Corrupt_file_is_backed_up_and_defaults_used(string content)
    {
        WriteSettings(content);

        var s = New().Load();

        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
        Assert.Equal(content, File.ReadAllText(SettingsPath + ".bad"));
        Assert.Equal(content, File.ReadAllText(SettingsPath)); // the user's file is left alone
    }

    [Theory]
    [InlineData("""{ "activation": { "tap": "Teleport" } }""")]
    [InlineData("""{ "activation": { "tap": 42 } }""")]
    [InlineData("""{ "window": { "theme": 7 } }""")]
    public void A_bad_field_keeps_its_default_and_the_file_is_left_alone(string content)
    {
        WriteSettings(content);

        var store = New();
        var s = store.Load();

        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
        Assert.Equal(ThemeChoice.System, s.Window.Theme);
        Assert.Single(store.SettingsProblems);
        Assert.Equal(content, File.ReadAllText(SettingsPath)); // not rewritten: the user's wording stays until they fix it
    }

    [Theory]
    [InlineData(10, 320)]
    [InlineData(99999, 4000)]
    public void Window_size_is_clamped(int width, int expected)
    {
        WriteSettings($$"""{ "window": { "minWidth": {{width}} } }""");
        Assert.Equal(expected, New().Load().Window.MinWidth);
    }

    [Fact]
    public void Percentages_and_widths_are_clamped()
    {
        WriteSettings("""{ "window": { "widthPercent": 500, "maxHeightPercent": 5, "minWidth": 50, "maxWidth": 10 } }""");
        var w = New().Load().Window;
        Assert.Equal(90, w.WidthPercent);
        Assert.Equal(30, w.MaxHeightPercent);
        Assert.Equal(320, w.MinWidth);
        Assert.Equal(320, w.MaxWidth); // never below MinWidth
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        WriteSettings(
            """
            {
              // my hotkey
              "activation": { "fallbackHotkey": "Ctrl+Alt+H", },
            }
            """);
        Assert.Equal("Ctrl+Alt+H", New().Load().Activation.FallbackHotkey);
    }

    [Fact]
    public void Locked_file_yields_in_memory_defaults_without_throwing()
    {
        WriteSettings("""{ "window": { "minWidth": 900 } }""");
        using var lockHandle = new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var s = New().Load();

        Assert.Equal(460, s.Window.MinWidth);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(5.0, 1.0)]
    public void Opacities_are_clamped_to_unit_range(double value, double expected)
    {
        WriteSettings($$"""{ "window": { "tintOpacity": {{value}}, "luminosityOpacity": {{value}} } }""");
        var s = New().Load();
        Assert.Equal(expected, s.Window.TintOpacity);
        Assert.Equal(expected, s.Window.LuminosityOpacity);
    }

    [Fact]
    public void New_options_are_written_into_an_existing_file_with_backup()
    {
        var store = new SettingsStore(_dir);
        store.Save(new HotlineSettings());
        var path = store.FilePath;
        var json = File.ReadAllText(path).Replace("\"defaultPrompt\": \"default\",", "");
        File.WriteAllText(path, json);
        store.Load();
        Assert.Contains("\"defaultPrompt\"", File.ReadAllText(path));
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Complete_file_is_not_rewritten()
    {
        var store = new SettingsStore(_dir);
        store.Save(new HotlineSettings());
        var path = store.FilePath;
        File.WriteAllText(path, "// my notes\n" + File.ReadAllText(path));
        store.Load();
        Assert.StartsWith("// my notes", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void TryRead_never_touches_a_broken_file()
    {
        var store = new SettingsStore(_dir);
        store.Save(new HotlineSettings());
        File.WriteAllText(store.FilePath, "{ \"window\": { \"fontSize\": 1");
        Assert.Null(store.TryRead());
        Assert.Equal("{ \"window\": { \"fontSize\": 1", File.ReadAllText(store.FilePath));
        Assert.False(File.Exists(store.FilePath + ".bad"));
    }

    [Fact]
    public void An_old_schema_file_is_read_as_it_is_without_an_archive_and_connections_stay_untouched()
    {
        Directory.CreateDirectory(Path.Combine(_dir, SettingsStore.ConnectionsFolder));
        var conn = Path.Combine(_dir, SettingsStore.ConnectionsFolder, "a.json");
        const string connText = "{ \"id\": \"a\", \"type\": \"gemini\", \"name\": \"A\" } // mine";
        File.WriteAllText(conn, connText);
        // Values the removed upgrade steps used to move: hold, font size 14, heights 120/320/520, min width 600, no default backend.
        WriteSettings("""
            { "schemaVersion": 3,
              "activation": { "hold": "showPopup" },
              "window": { "fontSize": 14, "height": 520, "minWidth": 600 },
              "chat": { "defaultBackend": "" } }
            """);

        var s = New().Load();

        Assert.Equal(KeyAction.ShowPopup, s.Activation.Hold);
        Assert.Equal(14, s.Window.FontSize);
        Assert.Equal(520, s.Window.Height);
        Assert.Equal(600, s.Window.MinWidth);
        Assert.Equal("", s.Chat.DefaultBackend);
        Assert.Equal("a", Assert.Single(s.Chat.Backends).Id); // no built-in connection added for an old file
        Assert.Equal(HotlineSettings.CurrentSchemaVersion, s.SchemaVersion); // stamped in memory, written with the next save
        Assert.Empty(Directory.GetFiles(_dir, "*.old*"));
        Assert.Equal(connText, File.ReadAllText(conn));
    }

    [Fact]
    public void A_file_without_a_schema_version_is_not_archived()
    {
        WriteSettings("""{ "window": { "width": 700 } }""");
        New().Load();
        Assert.Empty(Directory.GetFiles(_dir, "*.old*"));
    }

    [Fact]
    public void An_inline_backends_list_is_still_split_into_connection_files_without_an_old_archive()
    {
        WriteSettings("""{ "schemaVersion": 3, "chat": { "defaultBackend": "g", "backends": [ { "id": "g", "type": "gemini", "name": "G" } ] } }""");

        var store = New();
        var s = store.Load();

        Assert.Equal("g", Assert.Single(s.Chat.Backends).Id);
        Assert.True(File.Exists(store.ConnectionPath("g")));
        Assert.DoesNotContain("backends", File.ReadAllText(SettingsPath));
        Assert.True(File.Exists(SettingsPath + ".bak")); // the hand-edited file is still backed up before the rewrite
        Assert.Empty(Directory.GetFiles(_dir, "*.old*"));
    }

    // ---- connection files (regressions from the full-codebase review) ---------------------------

    [Fact]
    public void Duplicate_connection_ids_in_an_old_backends_list_do_not_break_a_reload()
    {
        WriteSettings("""{ "schemaVersion": 7, "chat": { "backends": [ { "id": "a", "type": "local", "name": "One" }, { "id": "A", "type": "local", "name": "Two" } ] } }""");
        var store = new SettingsStore(_dir);
        var service = new SettingsService(store, store.Load(), new FileLog(Path.Combine(_dir, "h.log")));
        var edited = store.TryRead()!;
        edited.Window.FontSize = 20;
        Assert.True(service.Reload(edited));
        Assert.Single(service.Current.Chat.Backends);
    }

    [Fact]
    public void A_connection_file_added_while_running_is_deleted_with_its_connection()
    {
        Directory.CreateDirectory(_dir);
        var store = new SettingsStore(_dir);
        var s = store.Load();
        Directory.CreateDirectory(Path.Combine(_dir, SettingsStore.ConnectionsFolder));
        File.WriteAllText(Conn("late"), """{ "type": "local", "name": "Late" }""");
        s.Chat.Backends = store.TryRead()!.Chat.Backends; // what a live reload takes in
        Assert.Contains(s.Chat.Backends, b => b.Id == "late");
        ConnectionEditor.Remove(s.Chat, "late");
        store.Save(s);
        Assert.False(File.Exists(Conn("late")));
    }

    [Fact]
    public void A_read_only_connection_file_does_not_stop_saving()
    {
        Directory.CreateDirectory(Path.Combine(_dir, SettingsStore.ConnectionsFolder));
        File.WriteAllText(Conn("ro"), """{ "type": "local", "name": "Ro" }""");
        var store = new SettingsStore(_dir);
        var s = store.Load();
        File.SetAttributes(Conn("ro"), FileAttributes.ReadOnly);
        ConnectionEditor.Remove(s.Chat, "ro");
        store.Save(s); // must not throw UnauthorizedAccessException
        Assert.True(File.Exists(Conn("ro")));
    }

    [Fact]
    public void Saving_never_exposes_a_null_connection_list_to_other_threads()
    {
        Directory.CreateDirectory(Path.Combine(_dir, SettingsStore.ConnectionsFolder));
        File.WriteAllText(Conn("a"), """{ "type": "local", "name": "A" }""");
        var store = new SettingsStore(_dir);
        var s = store.Load();
        var seenNull = false;
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() => { while (!stop.IsCancellationRequested) if (s.Chat.Backends is null) seenNull = true; });
        for (var i = 0; i < 3000; i++) SettingsStore.Serialize(s);
        stop.Cancel();
        reader.Wait();
        Assert.False(seenNull);
    }
}
