using System.Text.Json.Nodes;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Schema upgrades preserve connections and migrate the old scaled width units once.</summary>
public sealed class SettingsV14Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private SettingsStore New() => new(_dir);
    private string SettingsPath => Path.Combine(_dir, SettingsStore.FileName);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private void WriteSettings(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, json);
    }

    private static JsonObject Activation(string settingsText)
        => JsonNode.Parse(settingsText, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!["activation"]!.AsObject();

    [Theory]
    [InlineData("13", "\"minWidth\":600,\"maxWidth\":1000", 400, 1200, null, null, "")]
    [InlineData("13", "\"minWidth\":600", 400, 1200, null, null, "")]
    [InlineData("13", "\"maxWidth\":1000", 400, 1200, null, null, "")]
    [InlineData("13", "\"minWidth\":700", 700, 1200, null, null, "minWidth")]
    [InlineData("13", "\"width\":700", 400, 1200, null, null, "width")]
    [InlineData("13", "\"minWidth\":700,\"maxWidth\":1500", 700, 1500, null, null, "minWidth,maxWidth")]
    [InlineData("13", "\"width\":700,\"chatWidth\":900", 400, 1200, null, null, "width,chatWidth")]
    [InlineData("13", "\"minWidth\":1,\"maxWidth\":99999", 200, 16000, null, null, "minWidth,maxWidth")]
    [InlineData("14", "\"minWidth\":600,\"maxWidth\":1000,\"width\":700,\"chatWidth\":900", 600, 1000, 700, 900, "")]
    [InlineData(null, "\"minWidth\":600,\"maxWidth\":1000,\"width\":700,\"chatWidth\":900", 600, 1000, 700, 900, "")]
    public void Width_units_migrate_only_explicit_pre_v14_files(string? schema, string window, int min, int max, int? width, int? chatWidth, string noticed)
    {
        var original = "{" + (schema is null ? "" : $"\"schemaVersion\":{schema},") + "\"window\":{" + window + "}}";
        WriteSettings(original);
        var liveStore = New();
        var live = liveStore.TryRead()!;
        Assert.Equal(original, File.ReadAllText(SettingsPath)); // live reads never write
        Assert.Empty(Directory.GetFiles(_dir, "*.bak"));
        var store = New();
        var loaded = store.Load();
        Assert.Equal((min, max, width, chatWidth), (loaded.Window.MinWidth, loaded.Window.MaxWidth, loaded.Window.Width, loaded.Window.ChatWidth));
        Assert.Equal(SettingsStore.Serialize(loaded), SettingsStore.Serialize(live));
        Assert.Equal(15, loaded.SchemaVersion);
        foreach (var key in noticed.Split(',', StringSplitOptions.RemoveEmptyEntries))
            Assert.Contains(store.SettingsProblems, p => p.Contains("now real pixels") && p.Contains(key));
        if (noticed.Length == 0) Assert.Empty(store.SettingsProblems);
        if (schema == "13" && !window.Contains("99999"))
        {
            Assert.Equal(original, File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "settings.json.*.bak"))));
            var again = New();
            Assert.Equal(SettingsStore.Serialize(loaded), SettingsStore.Serialize(again.Load()));
            Assert.Empty(again.SettingsProblems); // stamped file never migrates or warns again
        }
    }

    [Fact]
    public void Width_migration_does_not_write_when_the_backup_fails()
    {
        const string original = """{"schemaVersion":13,"window":{"width":700,"minWidth":600,"maxWidth":1000}}""";
        WriteSettings(original);
        var store = New();
        store.BackupOverride = _ => throw new IOException("test backup failure");
        Assert.Null(store.Load().Window.Width);
        Assert.Equal(original, File.ReadAllText(SettingsPath));
        Assert.NotEmpty(store.WriteProblems);
        Assert.Empty(Directory.GetFiles(_dir, "*.bak"));
    }

    [Fact]
    public void A_complete_v13_file_is_migrated_and_stamped_even_without_missing_options()
    {
        var old = new HotlineSettings { SchemaVersion = 13 };
        old.Window.MinWidth = 600;
        old.Window.MaxWidth = 1000;
        old.Window.Width = 700;
        var original = SettingsStore.Serialize(old);
        WriteSettings(original);
        var store = New();
        store.Load();
        var saved = JsonNode.Parse(File.ReadAllText(SettingsPath))!;
        Assert.Equal(15, (int)saved["schemaVersion"]!);
        Assert.Equal(400, (int)saved["window"]!["minWidth"]!);
        Assert.Equal(1200, (int)saved["window"]!["maxWidth"]!);
        Assert.Null(saved["window"]!["width"]);
        Assert.Single(store.SettingsProblems);
        Assert.Equal(original, File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "settings.json.*.bak"))));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("\"rightCtrl\"")]
    [InlineData("\"RIGHTCTRL\"")]
    [InlineData("\"somethingElse\"")]
    [InlineData("{ \"mode\": \"rightCtrl\" }")]
    [InlineData("[1, \"x\", null]")]
    public void CopilotKey_of_any_value_is_dropped_without_failing_and_the_file_kept_as_bak(string value)
    {
        var original = $$"""
            {
              // my settings
              "schemaVersion": 13,
              "activation": { "copilotKey": {{value}}, "fallbackHotkey": "Ctrl+Alt+K" }
            }
            """;
        WriteSettings(original);

        var s = New().Load();

        Assert.Equal(15, s.SchemaVersion);
        Assert.Equal("Ctrl+Alt+K", s.Activation.FallbackHotkey); // the file was read, not replaced by defaults
        Assert.False(File.Exists(SettingsPath + ".bad"));
        Assert.Equal(original, File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "settings.json.*.bak"))));
        Assert.Empty(Directory.GetFiles(_dir, "*.old*"));
        var rewritten = File.ReadAllText(SettingsPath);
        Assert.DoesNotContain("copilotKey", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.False(Activation(rewritten).ContainsKey("copilotKey"));
        Assert.Equal(15, (int)JsonNode.Parse(rewritten)!["schemaVersion"]!);
    }

    [Theory]
    [InlineData(13, 800)] // a width chosen before v14 is kept
    [InlineData(14, 600)] // and one chosen on v14
    public void Own_min_width_is_kept(int schema, int width)
    {
        WriteSettings($$"""{ "schemaVersion": {{schema}}, "window": { "minWidth": {{width}} } }""");
        Assert.Equal(width, New().Load().Window.MinWidth);
    }

    [Fact]
    public void DefaultBackend_in_other_casing_points_at_the_same_connection()
    {
        var connections = Path.Combine(_dir, SettingsStore.ConnectionsFolder);
        Directory.CreateDirectory(connections);
        File.WriteAllText(Path.Combine(connections, "a.json"), """{ "id": "a", "type": "gemini", "name": "A" }""");
        File.WriteAllText(Path.Combine(connections, "My-Claude.json"), """{ "id": "My-Claude", "type": "claudeCode", "name": "B" }""");
        WriteSettings("""{ "schemaVersion": 14, "chat": { "defaultBackend": "my-claude", "order": ["a", "My-Claude"] } }""");
        Assert.Equal("My-Claude", New().Load().Chat.DefaultBackend);
    }

    [Theory]
    [InlineData("\"defaultBackend\"", "Conn-ClaudeCode")]
    [InlineData("\"DefaultBackend\"", "conn-claudecode")]
    [InlineData("\"DEFAULTBACKEND\"", "CONN-CLAUDECODE")]
    public void V13_connections_of_every_type_are_left_byte_identical(string key, string defaultValue)
    {
        var connections = Path.Combine(_dir, SettingsStore.ConnectionsFolder);
        Directory.CreateDirectory(connections);
        var files = new Dictionary<string, byte[]>();
        void Put(string name, string text)
        {
            var path = Path.Combine(connections, name);
            File.WriteAllText(path, text);
            files[path] = File.ReadAllBytes(path);
        }

        var ids = new List<string>();
        foreach (var type in Enum.GetValues<BackendType>())
        {
            var id = "Conn-" + type;
            ids.Add(id);
            var typeName = char.ToLowerInvariant(type.ToString()[0]) + type.ToString()[1..];
            // Hand-written, not serializer output: LF endings, a comment, odd key order and casing, fields left out.
            Put(id + ".json",
                "{\n  // " + type + "\n  \"TYPE\": \"" + typeName + "\",\n  \"id\": \"" + id + "\", \"name\": \"My " + type + "\",\n"
                + "  \"model\": \"m-" + type + "\", \"effort\": \"high\", \"endpoint\": \"https://example.test/" + type + "\",\n"
                + "  \"tools\": \"inherit\", \"approveAllTools\": false, \"maxTokens\": 4096, \"temperature\": 0.5,\n}\n");
        }
        Put("no-id-field.json", "{ \"type\": \"gemini\", \"name\": \"From the file name\" }");
        Put("renamed-file.json", "{ \"id\": \"other-id\", \"type\": \"local\", \"name\": \"Different file name\" }");
        Put("no-name.json", "{ \"id\": \"no-name\", \"type\": \"codex\" }");
        Put("broken.json", "{ \"id\": \"broken\", \"type\": ");
        Put("bad-type.json", "{ \"id\": \"bad-type\", \"type\": \"noSuchType\" }");
        WriteSettings($$"""
            {
              "schemaVersion": 13,
              "activation": { "copilotKey": "rightCtrl" },
              "chat": { {{key}}: "{{defaultValue}}", "order": [{{string.Join(", ", ids.Select(i => $"\"{i}\""))}}] }
            }
            """);

        var store = New();
        var s = store.Load();

        foreach (var (path, bytes) in files)
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), $"{Path.GetFileName(path)} was rewritten");
        Assert.Equal(files.Keys.Order(), Directory.GetFiles(connections).Order()); // nothing added or removed
        Assert.Contains(store.BrokenConnectionFiles, f => f.EndsWith("broken.json"));
        Assert.Contains(store.BrokenConnectionFiles, f => f.EndsWith("bad-type.json"));
        foreach (var type in Enum.GetValues<BackendType>())
        {
            var b = Assert.Single(s.Chat.Backends, b => b.Id == "Conn-" + type);
            Assert.Equal(type, b.Type);
            Assert.Equal("m-" + type, b.Model);
            Assert.Equal("https://example.test/" + type, b.Endpoint);
        }
        // defaultBackend keeps pointing at the same connection (spelled as that connection's id, which the app matches).
        Assert.Equal("Conn-ClaudeCode", s.Chat.DefaultBackend);
        var reloaded = New().Load();
        Assert.Equal("Conn-ClaudeCode", reloaded.Chat.DefaultBackend);
        Assert.Equal(s.Chat.Backends.Select(b => b.Id), reloaded.Chat.Backends.Select(b => b.Id));
    }
}
