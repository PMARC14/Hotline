using System.Text.Json.Nodes;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Schema v14: the Right Ctrl mode is gone, the default width is 680, connections are never touched.</summary>
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

        Assert.Equal(14, s.SchemaVersion);
        Assert.Equal("Ctrl+Alt+K", s.Activation.FallbackHotkey); // the file was read, not replaced by defaults
        Assert.False(File.Exists(SettingsPath + ".bad"));
        Assert.Equal(original, File.ReadAllText(SettingsPath + ".bak"));
        Assert.Empty(Directory.GetFiles(_dir, "*.old*"));
        var rewritten = File.ReadAllText(SettingsPath);
        Assert.DoesNotContain("copilotKey", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.False(Activation(rewritten).ContainsKey("copilotKey"));
        Assert.Equal(14, (int)JsonNode.Parse(rewritten)!["schemaVersion"]!);
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
