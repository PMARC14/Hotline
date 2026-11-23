using Hotline.Core.Activation;
using Hotline.Core.Backends;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class SettingsStoreTests : TempDirTest
{
    private SettingsStore New() => new(_dir);
    private string SettingsPath => Path.Combine(_dir, SettingsStore.FileName);
    private string Conn(string id) => Path.Combine(_dir, SettingsStore.ConnectionsFolder, id + ".json");

    private void WriteSettings(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, json);
    }

    [Theory]
    [InlineData("width")]
    [InlineData("chatWidth")]
    [InlineData("chatHeight")]
    [InlineData("fontSize")]
    [InlineData("fontFamily")]
    public void Missing_options_use_defaults_without_writing(string nullableKey)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(SettingsStore.Serialize(SettingsStore.Normalize(new HotlineSettings())))!;
        var window = root["window"]!.AsObject();
        window.Remove(nullableKey);
        window.Remove("backdrop");
        var original = "// hand edited\n" + root.ToJsonString();
        WriteSettings(original);

        New().Load();

        Assert.Equal(original, File.ReadAllText(SettingsPath));
        Assert.Empty(SettingsTestFiles.Backups(_dir, "settings.json"));
        var saved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(SettingsPath), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
        Assert.False(saved["window"]!.AsObject().ContainsKey("backdrop"));
    }

    [Fact]
    public void Missing_file_returns_defaults_without_writing()
    {
        var store = New();
        var s = store.Load();
        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
        Assert.Equal(("40%", 320, 800, 0, 60.0), (s.Window.Width.ToString(), s.Window.MinWidth, s.Window.MaxWidth, s.Window.MinHeight, s.Window.MaxHeightPercent));
        Assert.Equal(BackdropKind.AcrylicThin, s.Window.Backdrop);
        Assert.InRange(s.Window.TintOpacity, 0.0, 0.3);
        Assert.False(s.Diagnostics.VerboseLogging);
        Assert.Equal(0.8, s.Window.Anchor.Y);
        Assert.Equal(HotlineSettings.CurrentSchemaVersion, s.SchemaVersion);
        Assert.Equal(1, s.SchemaVersion);
        Assert.False(File.Exists(store.FilePath));
        Assert.False(File.Exists(store.FilePath + ".old"));
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = New();
        var s = store.Load();
        s.Activation.Hold = KeyAction.RegionSelect;
        s.Activation.FallbackHotkey = "Ctrl+Alt+H";
        store.Save(s);

        var loaded = New().Load();
        Assert.Equal(KeyAction.RegionSelect, loaded.Activation.Hold);
        Assert.Equal("Ctrl+Alt+H", loaded.Activation.FallbackHotkey);
    }

    [Fact]
    public void File_uses_readable_enum_names()
    {
        var store = New();
        store.Save(store.Load());
        Assert.Contains("\"togglePopup\"", File.ReadAllText(store.FilePath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_properties_keep_defaults()
    {
        WriteSettings("""{ "version": 1, "window": { "minWidth": 800 } }""");
        var s = New().Load();
        Assert.Equal(800, s.Window.MinWidth);
        Assert.Equal(0, s.Window.MinHeight);
        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
    }

    [Fact]
    public void Null_sections_are_replaced_with_defaults()
    {
        WriteSettings("""{ "version": 1, "activation": null, "window": null, "diagnostics": null }""");
        var s = New().Load();
        Assert.NotNull(s.Activation);
        Assert.NotNull(s.Window);
        Assert.NotNull(s.Diagnostics);
    }

    [Theory]
    [InlineData("250", "3000", 320, 800)]
    [InlineData("\"20%\"", "\"90%\"", 320, 800)]
    public void Percentages_and_widths_are_clamped(string width, string chatWidth, int openingPixels, int chatPixels)
    {
        WriteSettings($$$"""{ "version": 1, "window": { "width": {{{width}}}, "chatWidth": {{{chatWidth}}}, "maxHeightPercent": 5 } }""");
        var w = New().Load().Window;
        Assert.Equal(30, w.MaxHeightPercent);
        Assert.Equal(openingPixels, Windowing.PanelWidth.ResolvePixels(w, 1000, 1));
        Assert.Equal(chatPixels, Windowing.PanelWidth.ResolvePixels(w, 1000, 1, chat: true));
    }

    [Fact]
    public void Default_connection_matches_connection_file_case_insensitively()
    {
        WriteSettings("""{ "version": 1, "chat": { "defaultConnection": "claude" } }""");
        Directory.CreateDirectory(Path.Combine(_dir, SettingsStore.ConnectionsFolder));
        File.WriteAllText(Conn("Claude"), """{ "version": 1, "type": "claudeCode", "name": "Claude" }""");
        var loaded = New().Load();
        Assert.Equal("Claude", loaded.Chat.DefaultBackend);
        Assert.Single(loaded.Chat.Connections);
        Assert.Equal("Claude", loaded.Chat.Connections[0].Id);
    }

    [Theory]
    [InlineData("100")]
    [InlineData("5000")]
    [InlineData("\"5%\"")]
    [InlineData("\"95%\"")]
    public void Out_of_range_v1_widths_use_defaults_and_report_problems(string width)
    {
        WriteSettings($$$"""{"version":1,"window":{"width":{{{width}}},"chatWidth":{{{width}}},"minWidth":50,"maxWidth":10}}""");
        var store = New();
        var w = store.Load().Window;
        Assert.Equal(new WindowSettings().Width, w.Width);
        Assert.Null(w.ChatWidth);
        Assert.Equal((200, 200), (w.MinWidth, w.MaxWidth));
        Assert.Contains(store.SettingsProblems, p => p.Contains("window.width", StringComparison.Ordinal));
        Assert.Contains(store.SettingsProblems, p => p.Contains("window.chatWidth", StringComparison.Ordinal));
    }

    [Fact]
    public void Locked_file_yields_in_memory_defaults_without_throwing()
    {
        WriteSettings("""{ "version": 1, "window": { "minWidth": 900 } }""");
        using var lockHandle = new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var s = New().Load();

        Assert.Equal(320, s.Window.MinWidth);
    }

    [Fact]
    public void Missing_options_are_never_written_on_load()
    {
        var store = new SettingsStore(_dir);
        store.Save(new HotlineSettings());
        var path = store.FilePath;
        var json = File.ReadAllText(path).Replace("\"defaultPrompt\": \"default\",", "");
        File.WriteAllText(path, json);
        store.Load();
        Assert.DoesNotContain("\"defaultPrompt\"", File.ReadAllText(path));
        Assert.Empty(SettingsTestFiles.Backups(_dir, "settings.json"));
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
    public void A_connection_file_added_while_running_is_deleted_with_its_connection()
    {
        Directory.CreateDirectory(_dir);
        var store = new SettingsStore(_dir);
        var s = store.Load();
        store.Save(s);
        Directory.CreateDirectory(Path.Combine(_dir, SettingsStore.ConnectionsFolder));
        File.WriteAllText(Conn("late"), """{ "version": 1, "type": "local", "name": "Late" }""");
        s.Chat.Connections = store.TryRead()!.Chat.Connections; // what a live reload takes in
        Assert.Contains(s.Chat.Connections, b => b.Id == "late");
        ConnectionEditor.Remove(s.Chat, "late");
        store.Save(s);
        Assert.False(File.Exists(Conn("late")));
    }

    [Fact]
    public void A_read_only_connection_file_does_not_stop_saving()
    {
        Directory.CreateDirectory(Path.Combine(_dir, SettingsStore.ConnectionsFolder));
        File.WriteAllText(Conn("ro"), """{ "version": 1, "type": "local", "name": "Ro" }""");
        var store = new SettingsStore(_dir);
        var s = store.Load();
        File.SetAttributes(Conn("ro"), FileAttributes.ReadOnly);
        ConnectionEditor.Remove(s.Chat, "ro");
        store.Save(s); // must not throw UnauthorizedAccessException
        Assert.True(File.Exists(Conn("ro")));
    }

    [Fact]
    public async Task Saving_never_exposes_a_null_connection_list_to_other_threads()
    {
        var files = new Files.FakeFileSystem();
        var store = new SettingsStore(_dir, files);
        var s = new HotlineSettings { Chat = new() { Connections = [new() { Id = "a", Type = BackendType.Local }] } };
        var connections = s.Chat.Connections;
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var inspected = new ManualResetEventSlim();
        files.Fail = call =>
        {
            if (call.Operation == nameof(Core.Files.IFileSystem.WriteDurable) && call.Path == store.FilePath)
            {
                writing.TrySetResult();
                if (!inspected.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Reader did not inspect the live list.");
            }
            return false;
        };
        var save = Task.Run(() => store.Save(s), TestContext.Current.CancellationToken);
        try
        {
            await writing.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Same(connections, s.Chat.Connections);
            Assert.Equal("a", Assert.Single(s.Chat.Connections).Id);
        }
        finally { inspected.Set(); }
        await save;
        Assert.Empty(store.WriteProblems);
        Assert.True(files.Files.ContainsKey(Conn("a")));
    }

    [Fact]
    public void TestLinks_DefaultsFalseAndReadsExplicitTrue()
    {
        WriteSettings("""{ "version": 1, "diagnostics": { "verboseLogging": false } }""");
        var absent = New();
        Assert.False(absent.Load().Diagnostics.TestLinks);
        Assert.Empty(absent.SettingsProblems);

        WriteSettings("""{ "version": 1, "diagnostics": { "testLinks": false } }""");
        var explicitFalse = New();
        Assert.False(explicitFalse.Load().Diagnostics.TestLinks);
        Assert.Empty(explicitFalse.SettingsProblems);

        WriteSettings("""{ "version": 1, "diagnostics": { "testLinks": true } }""");
        var explicitTrue = New();
        Assert.True(explicitTrue.Load().Diagnostics.TestLinks);
        Assert.Empty(explicitTrue.SettingsProblems);

        WriteSettings("""{ "version": 1, "diagnostics": { "testLinks": "yes" } }""");
        var invalid = New();
        Assert.False(invalid.Load().Diagnostics.TestLinks);
        Assert.Contains(invalid.SettingsProblems, p => p.Contains("diagnostics.testLinks", StringComparison.Ordinal));
    }
}
