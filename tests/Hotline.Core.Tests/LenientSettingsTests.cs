using System.Text;
using System.Text.Json.Nodes;
using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>A broken settings file costs only the broken field: the rest of the file applies and one warning says what.</summary>
public sealed class LenientSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private string SettingsPath => Path.Combine(_dir, SettingsStore.FileName);
    private string Conn(string id) => Path.Combine(_dir, "connections", id + ".json");
    private void Write(string text) { Directory.CreateDirectory(_dir); File.WriteAllText(SettingsPath, text); }
    private void WriteConn(string id, string text) { Directory.CreateDirectory(Path.Combine(_dir, "connections")); File.WriteAllText(Conn(id), text); }

    // Settings the tests set in the file to prove the rest of it is still read: (path, JSON, the same change on the expected settings).
    private static readonly (string Path, string Json, Action<HotlineSettings> Apply)[] Markers =
    [
        ("general.language", "\"de\"", s => s.General.Language = "de"),
        ("diagnostics.verboseLogging", "true", s => s.Diagnostics.VerboseLogging = true),
        ("window.hideOnBlur", "false", s => s.Window.HideOnBlur = false),
        ("window.anchor.y", "0.3", s => s.Window.Anchor.Y = 0.3),
        ("chat.saveHistory", "false", s => s.Chat.SaveHistory = false),
        ("activation.fallbackHotkey", "\"Ctrl+Alt+H\"", s => s.Activation.FallbackHotkey = "Ctrl+Alt+H"),
    ];

    private static void SetPath(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        var node = root;
        foreach (var part in parts[..^1]) node = (JsonObject)(node[part] ??= new JsonObject());
        node[parts[^1]] = value;
    }

    /// <summary>A file with the markers plus the bad value at <paramref name="badPath"/>, and the settings it must produce apart from that.</summary>
    private (string Text, HotlineSettings Expected) FileWith(string badPath, string badJson)
    {
        var root = new JsonObject();
        var expected = new HotlineSettings();
        foreach (var (path, json, apply) in Markers)
        {
            if (path == badPath || path.StartsWith(badPath + ".", StringComparison.Ordinal)) continue; // inside the broken part
            SetPath(root, path, JsonNode.Parse(json));
            apply(expected);
        }
        SetPath(root, badPath, JsonNode.Parse(badJson));
        return (root.ToJsonString(), SettingsStore.Normalize(expected));
    }

    public static TheoryData<string, string> BadFields => new()
    {
        // unknown enum words, and numbers where an enum belongs
        { "window.theme", "\"mauve\"" }, { "window.theme", "7" }, { "window.theme", "null" },
        { "window.backdrop", "\"glass\"" }, { "window.layout", "[]" }, { "window.scrollbar", "{\"a\":1}" },
        { "window.growDirection", "true" }, { "activation.tap", "\"Teleport\"" }, { "activation.hold", "42" },
        { "chat.growMode", "\"huge\"" }, { "chat.ocr", "1" }, { "chat.memoryTool", "\"sometimes\"" },
        // a number given as a string, a string given as a number, null where it isn't allowed
        { "window.widthPercent", "\"25\"" }, { "window.tintOpacity", "\"0.5\"" }, { "window.fontSize", "\"big\"" },
        { "window.fontSize", "true" }, { "window.accent", "5" }, { "window.openSound", "null" },
        { "window.alwaysOnTop", "\"yes\"" }, { "window.fontFamily", "7" }, { "general.language", "5" },
        { "general.language", "null" }, { "general.supportReminder", "\"no\"" }, { "activation.fallbackHotkey", "5" },
        { "chat.defaultPrompt", "null" }, { "chat.thinkingDelayMs", "\"700\"" }, { "chat.order", "\"a\"" },
        { "chat.order", "[1,2]" }, { "chat.stopShortcut", "\"Banana+Pear\"" },
        // numbers that don't fit, fractions where a whole number belongs
        { "window.maxWidth", "99999999999" }, { "window.height", "3.5" }, { "window.minWidth", "1e999" },
        { "chat.maxImagePixels", "-99999999999" }, { "chat.historyListSize", "1e30" },
        // an object where a scalar belongs, a scalar or array where an object belongs
        { "window.width", "{\"x\":1}" }, { "window.anchor", "\"top\"" }, { "window.anchor.x", "\"left\"" },
        { "window.anchor.monitor", "[1]" }, { "diagnostics", "[]" }, { "diagnostics.verboseLogging", "{}" },
        { "general", "5" }, { "chat", "\"x\"" },
    };

    [Theory]
    [MemberData(nameof(BadFields))]
    public void One_bad_field_keeps_its_default_and_the_rest_of_the_file_applies(string badPath, string badJson)
    {
        var (text, expected) = FileWith(badPath, badJson);
        Write(text);

        var store = new SettingsStore(_dir);
        var loaded = store.Load();

        Assert.Equal(SettingsStore.Serialize(expected), SettingsStore.Serialize(loaded));
        var problem = Assert.Single(store.SettingsProblems);
        Assert.Contains(badPath, problem);
        Assert.Null(store.SettingsError);
        Assert.Equal(text, File.ReadAllText(SettingsPath)); // the user's file is not rewritten
        // the same file read live gives the same answer
        var live = new SettingsStore(_dir).TryRead();
        Assert.NotNull(live);
        Assert.Equal(SettingsStore.Serialize(expected), SettingsStore.Serialize(live));
    }

    [Theory]
    [InlineData("window.fontSize", "5", 10)]
    [InlineData("window.fontSize", "500", 32)]
    [InlineData("chat.maxImagePixels", "10", 256)]
    public void A_number_outside_its_limits_is_reported_and_pulled_in(string path, string json, int pulledTo)
    {
        var (text, _) = FileWith(path, json);
        Write(text);
        var store = new SettingsStore(_dir);
        var s = store.Load();
        var problem = Assert.Single(store.SettingsProblems);
        Assert.Contains(path, problem);
        Assert.Equal(pulledTo, path == "window.fontSize" ? s.Window.FontSize : s.Chat.MaxImagePixels);
    }

    [Fact]
    public void The_warning_names_the_file_the_field_and_a_short_clean_value()
    {
        var long_ = new string('x', 200);
        Write($$"""{ "window": { "backdrop": "glass\nINFO forged {{long_}}" } }""");
        var store = new SettingsStore(_dir);
        store.Load();
        var problem = Assert.Single(store.SettingsProblems);
        Assert.Contains("settings.json", problem);
        Assert.Contains("window.backdrop", problem);
        Assert.Contains("glass", problem);
        Assert.DoesNotContain('\n', problem);
        Assert.True(problem.Length < 300, problem);
    }

    [Fact]
    public void A_secret_shaped_value_is_never_quoted_whole()
    {
        Write("""{ "window": { "backdrop": "sk-abcdefghijklmnopqrstuvwxyz0123456789" } }""");
        var store = new SettingsStore(_dir);
        store.Load();
        Assert.DoesNotContain("abcdefghijklmnop", Assert.Single(store.SettingsProblems));
    }

    [Fact]
    public void At_most_fifty_problems_are_recorded()
    {
        var items = string.Join(",", Enumerable.Range(0, 300).Select(i => $"{{\"type\":\"nope{i}\"}}"));
        Write($$"""{ "chat": { "backends": [{{items}}] } }""");
        var store = new SettingsStore(_dir);
        store.Load();
        Assert.Equal(ReadProblems.Max, store.SettingsProblems.Count);
    }

    [Fact]
    public void A_key_given_twice_uses_the_last_and_says_so()
    {
        Write("""{ "window": { "theme": "dark", "Theme": "light", "hideOnBlur": false } }""");
        var store = new SettingsStore(_dir);
        var s = store.Load();
        Assert.Equal(ThemeChoice.Light, s.Window.Theme);
        Assert.False(s.Window.HideOnBlur);
        Assert.Contains("window.theme", Assert.Single(store.SettingsProblems));

        Write("""{ "window": { "theme": "dark" }, "Window": { "hideOnBlur": false } }""");
        var again = new SettingsStore(_dir);
        var t = again.Load(); // a section given twice: the last one is the section, as before
        Assert.Equal(ThemeChoice.System, t.Window.Theme);
        Assert.False(t.Window.HideOnBlur);
        Assert.Contains("window", Assert.Single(again.SettingsProblems));
    }

    [Fact]
    public void Comments_trailing_commas_and_a_byte_order_mark_are_fine_and_silent()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "// my settings\n{ /* c */ \"window\": { \"theme\": \"dark\", }, }", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var store = new SettingsStore(_dir);
        var s = store.Load();
        Assert.Equal(ThemeChoice.Dark, s.Window.Theme);
        Assert.Empty(store.SettingsProblems);
        Assert.Null(store.SettingsError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t ")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("{ \"window\": { \"theme\": \"dark\"")]
    public void A_file_that_is_not_a_json_object_is_kept_as_it_is_and_reported(string content)
    {
        Write(content);
        var store = new SettingsStore(_dir);
        var s = store.Load();
        Assert.Equal(ThemeChoice.System, s.Window.Theme);
        Assert.NotNull(store.SettingsError);
        Assert.True(store.SettingsFileDamaged);
        Assert.Equal(content, File.ReadAllText(SettingsPath));
        Assert.Equal(content, File.ReadAllText(SettingsPath + ".bad"));
        Assert.Null(new SettingsStore(_dir).TryRead()); // live: the settings in force stay
    }

    [Fact]
    public void A_start_with_the_same_broken_file_does_not_pile_up_copies()
    {
        Write("{ broken");
        new SettingsStore(_dir).Load();
        new SettingsStore(_dir).Load();
        Assert.Single(Directory.GetFiles(_dir, "settings.json*.bad"));
    }

    [Fact]
    public void A_five_megabyte_file_with_one_bad_field_is_read()
    {
        var padding = new string('p', 5 * 1024 * 1024);
        Write($$"""{ "note": "{{padding}}", "window": { "theme": "mauve", "hideOnBlur": false } }""");
        var store = new SettingsStore(_dir);
        var s = store.Load();
        Assert.False(s.Window.HideOnBlur);
        Assert.Single(store.SettingsProblems);
    }

    [Fact]
    public void A_five_megabyte_value_in_a_known_field_stays_short_in_the_warning()
    {
        Write($$"""{ "window": { "backdrop": "{{new string('z', 5 * 1024 * 1024)}}" } }""");
        var store = new SettingsStore(_dir);
        store.Load();
        Assert.True(Assert.Single(store.SettingsProblems).Length < 300);
    }

    [Fact]
    public void Very_deep_nesting_is_a_broken_file_not_a_crash()
    {
        Write(new string('[', 5000) + new string(']', 5000));
        var store = new SettingsStore(_dir);
        Assert.Equal(ThemeChoice.System, store.Load().Window.Theme);
        Assert.NotNull(store.SettingsError);

        Write("{ \"window\": { \"fontSize\": " + new string('[', 40) + new string(']', 40) + " } }"); // deep, but inside the limit
        var store2 = new SettingsStore(_dir);
        Assert.Null(store2.Load().Window.FontSize);
        Assert.Single(store2.SettingsProblems);
    }

    [Fact]
    public void Invalid_utf8_inside_a_string_is_replaced_and_elsewhere_is_a_broken_file()
    {
        Directory.CreateDirectory(_dir);
        var inString = Encoding.UTF8.GetBytes("{ \"window\": { \"accent\": \"ab").Concat(new byte[] { 0xFF, 0xFE }).Concat(Encoding.UTF8.GetBytes("\", \"hideOnBlur\": false } }")).ToArray();
        File.WriteAllBytes(SettingsPath, inString);
        var store = new SettingsStore(_dir);
        Assert.False(store.Load().Window.HideOnBlur);
        Assert.Null(store.SettingsError);

        var outside = Encoding.UTF8.GetBytes("{ \"window\": ").Concat(new byte[] { 0xFF, 0xFE }).Concat(Encoding.UTF8.GetBytes("{} }")).ToArray();
        File.WriteAllBytes(SettingsPath, outside);
        var store2 = new SettingsStore(_dir);
        store2.Load();
        Assert.NotNull(store2.SettingsError);
    }

    // ---- live reload -----------------------------------------------------------------------------

    [Fact]
    public void A_half_saved_file_keeps_the_settings_in_force_and_is_reported_once_per_state()
    {
        var store = new SettingsStore(_dir);
        var s = store.Load();
        s.Window.Theme = ThemeChoice.Dark;
        store.Save(s);
        Assert.NotNull(store.TryRead());
        Assert.Equal("", ConfigProblems.Key(store.SettingsError, store.SettingsProblems));

        Write("{ \"window\": { \"theme\": ");
        Assert.Null(store.TryRead());
        var first = ConfigProblems.Key(store.SettingsError, store.SettingsProblems);
        Assert.NotEqual("", first);
        Assert.Null(store.TryRead());
        Assert.Equal(first, ConfigProblems.Key(store.SettingsError, store.SettingsProblems)); // the same state: no second notice

        Write("""{ "window": { "theme": "light" } }""");
        Assert.Equal(ThemeChoice.Light, store.TryRead()!.Window.Theme);
        Assert.Equal("", ConfigProblems.Key(store.SettingsError, store.SettingsProblems)); // fixed: the notice goes
    }

    [Fact]
    public void A_live_edit_with_one_bad_field_applies_the_rest_and_reports_it()
    {
        var store = new SettingsStore(_dir);
        store.Load();
        Write("""{ "window": { "backdrop": "glass", "theme": "dark" } }""");
        var live = store.TryRead();
        Assert.NotNull(live);
        Assert.Equal(ThemeChoice.Dark, live.Window.Theme);
        Assert.Equal(BackdropKind.AcrylicThin, live.Window.Backdrop);
        Assert.Contains("window.backdrop", Assert.Single(store.SettingsProblems));
    }

    // ---- saving over a damaged file --------------------------------------------------------------

    [Fact]
    public void Saving_after_a_lenient_read_writes_a_valid_file_and_keeps_the_damaged_one()
    {
        var damaged = """{ "window": { "backdrop": "glass", "theme": "dark" } } // mine""";
        Write(damaged);
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        var service = new SettingsService(store, settings, new FileLog(Path.Combine(_dir, "log.txt")));

        service.Update(s => s.Window.Theme = ThemeChoice.Light);

        var copy = Assert.Single(Directory.GetFiles(_dir, "settings.json.*.bak"));
        Assert.Equal(damaged, File.ReadAllText(copy));
        var again = new SettingsStore(_dir);
        Assert.Equal(ThemeChoice.Light, again.Load().Window.Theme);
        Assert.Empty(again.SettingsProblems);
        Assert.Empty(service.Problems);
    }

    [Fact]
    public void Saving_after_a_syntax_error_keeps_a_copy_of_the_broken_file()
    {
        Write("{ not json");
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        var service = new SettingsService(store, settings, new FileLog(Path.Combine(_dir, "log.txt")));
        service.Update(s => s.Window.Theme = ThemeChoice.Light);
        Assert.Equal("{ not json", File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "settings.json.*.bak"))));
        Assert.Equal(ThemeChoice.Light, new SettingsStore(_dir).Load().Window.Theme);
    }

    [Fact]
    public void A_file_with_ignored_values_is_not_rewritten_at_start_for_new_options()
    {
        Write("""{ "window": { "backdrop": "glass" } }""");
        new SettingsStore(_dir).Load();
        Assert.Equal("""{ "window": { "backdrop": "glass" } }""", File.ReadAllText(SettingsPath));
        Assert.Empty(Directory.GetFiles(_dir, "*.bak"));
    }

    // ---- connection files ------------------------------------------------------------------------

    [Theory]
    [InlineData("maxTokens", "\"lots\"")]
    [InlineData("maxTokens", "99999999999")]
    [InlineData("temperature", "\"warm\"")]
    [InlineData("keepCliSessions", "\"no\"")]
    [InlineData("tools", "\"everything\"")]
    [InlineData("approveAllTools", "\"true\"")]
    [InlineData("approveAllTools", "1")]
    [InlineData("args", "\"--a --b\"")]
    [InlineData("args", "[1, 2]")]
    [InlineData("images", "\"yes\"")]
    [InlineData("endpoint", "5")]
    public void One_bad_connection_field_keeps_its_default_and_the_rest_of_the_file_applies(string key, string json)
    {
        WriteConn("c1", $$"""{ "type": "anthropic", "name": "Mine", "model": "opus", "{{key}}": {{json}} }""");
        var store = new SettingsStore(_dir);
        var c = Assert.Single(store.Load().Chat.Backends);
        var problem = Assert.Single(store.SettingsProblems);
        Assert.Contains("connections/c1.json", problem);
        Assert.Contains(key, problem);
        Assert.Equal(BackendType.Anthropic, c.Type);
        Assert.Equal("opus", c.Model);
        var fresh = new BackendProfile();
        Assert.False(c.ApproveAllTools);
        Assert.Equal(ToolMode.ChatOnly, c.Tools);
        Assert.Equal(fresh.KeepCliSessions, c.KeepCliSessions);
        Assert.Null(c.MaxTokens);
        Assert.Null(c.Args);
        Assert.Equal("Mine", c.Name);
    }

    [Fact]
    public void A_temperature_outside_its_limits_is_reported_and_pulled_in()
    {
        WriteConn("c1", """{ "type": "local", "temperature": 99 }""");
        var store = new SettingsStore(_dir);
        Assert.Equal(2, Assert.Single(store.Load().Chat.Backends).Temperature);
        Assert.Contains("temperature", Assert.Single(store.SettingsProblems));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("5")]
    [InlineData("null")]
    [InlineData("{}")]
    public void An_unknown_connection_type_lists_the_file_as_broken_and_never_guesses(string type)
    {
        var text = $$"""{ "type": {{(type is "nope" ? "\"nope\"" : type)}}, "name": "X" }""";
        WriteConn("odd", text);
        var store = new SettingsStore(_dir);
        var s = store.Load();
        Assert.Empty(s.Chat.Backends);
        Assert.Single(store.BrokenConnectionFiles);
        Assert.Contains("connections/odd.json", Assert.Single(store.SettingsProblems));
        store.Save(s);
        Assert.Equal(text, File.ReadAllText(Conn("odd"))); // never overwritten
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{ \"type\": ")]
    public void An_unparsable_connection_file_is_broken_reported_with_its_name_and_left_alone(string text)
    {
        WriteConn("bad", text);
        WriteConn("good", """{ "type": "local", "name": "Good" }""");
        var store = new SettingsStore(_dir);
        var s = store.Load();
        Assert.Equal("good", Assert.Single(s.Chat.Backends).Id);
        Assert.Equal(Conn("bad"), Assert.Single(store.BrokenConnectionFiles));
        Assert.Contains("connections/bad.json", Assert.Single(store.SettingsProblems));
        store.Save(s);
        Assert.Equal(text, File.ReadAllText(Conn("bad")));
    }

    [Fact]
    public void Two_files_with_one_id_report_the_second()
    {
        WriteConn("a", """{ "id": "same", "type": "local" }""");
        WriteConn("b", """{ "id": "same", "type": "local" }""");
        var store = new SettingsStore(_dir);
        Assert.Single(store.Load().Chat.Backends);
        Assert.Contains("connections/b.json", Assert.Single(store.SettingsProblems));
    }

    [Fact]
    public void A_connection_file_with_an_ignored_value_is_copied_before_a_save_rewrites_it()
    {
        var text = """{ "type": "local", "name": "Mine", "maxTokens": "lots" }""";
        WriteConn("c1", text);
        var store = new SettingsStore(_dir);
        var s = store.Load();
        s.Chat.Backends[0].Name = "Renamed";
        store.Save(s);
        Assert.Contains("Renamed", File.ReadAllText(Conn("c1")));
        Assert.Equal(text, File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_dir, "connections"), "c1.json.*.bak"))));
    }

    [Fact]
    public void A_fixed_connection_file_clears_its_problems_on_the_next_live_read()
    {
        WriteConn("c1", """{ "type": "nope" }""");
        var store = new SettingsStore(_dir);
        store.Load();
        Assert.Single(store.SettingsProblems);
        WriteConn("c1", """{ "type": "local" }""");
        Assert.Single(store.TryRead()!.Chat.Backends);
        Assert.Empty(store.SettingsProblems);
    }

    [Fact]
    public void A_bad_entry_in_the_old_inline_connection_list_is_skipped_alone()
    {
        Write("""{ "chat": { "backends": [ { "id": "ok", "type": "local" }, { "id": "odd", "type": "nope" }, 5 ] } }""");
        var store = new SettingsStore(_dir);
        Assert.Equal("ok", Assert.Single(store.Load().Chat.Backends).Id);
        Assert.Equal(2, store.SettingsProblems.Count);
    }
}
