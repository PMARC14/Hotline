using System.Text;
using System.Text.Json.Nodes;
using Hotline.Core.Files;
using Hotline.Core.Settings;
using Hotline.Core.Tests.Files;
using Hotline.Core.Windowing;
using Hotline.Core.State;

namespace Hotline.Core.Tests;

public sealed class SettingsV1Tests : TempDirTest
{
    [Theory]
    [InlineData("1.5")]
    [InlineData("1e20")]
    [InlineData("1e400")]
    [InlineData("2147483648")]
    public void Non_int32_newer_versions_load_known_values_and_are_never_written(string version)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "connections"));
        var settingsPath = Path.Combine(_dir, "settings.json");
        var connectionPath = Path.Combine(_dir, "connections", "Future.json");
        var main = $$$"""{"version":{{{version}}},"window":{"theme":"dark"}}""";
        var connection = $$$"""{"version":{{{version}}},"type":"local","name":"Future"}""";
        File.WriteAllText(settingsPath, main);
        File.WriteAllText(connectionPath, connection);
        var store = new SettingsStore(_dir);
        var settings = Assert.IsType<HotlineSettings>(store.TryRead());
        Assert.Equal(ThemeChoice.Dark, settings.Window.Theme);
        Assert.Equal("Future", Assert.Single(settings.Chat.Backends).Name);
        Assert.True(store.NewerVersionFile > 1);
        Assert.DoesNotContain(store.SettingsProblems, p => p.Contains(Text.Localization.Strings.Settings_PreFreeze, StringComparison.Ordinal));
        settings.Window.Theme = ThemeChoice.Light;
        settings.Chat.Backends[0].Name = "Edited";
        store.Save(settings);
        Assert.Equal(main, File.ReadAllText(settingsPath));
        Assert.Equal(connection, File.ReadAllText(connectionPath));
        settings.Chat.Backends.Clear();
        store.Save(settings, ["future"]);
        Assert.Equal(connection, File.ReadAllText(connectionPath));
        Assert.False(Directory.Exists(Path.Combine(_dir, "backups")));
    }

    [Fact]
    public void Removing_an_approval_rule_removes_only_that_map_entry()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"version":1,"tools":{"approvals":{"Server/Tool":"ALLOW","server/tool":"ask","future":42}}}""");
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        settings.Tools.Approvals.Remove("Server/Tool");
        store.Save(settings);
        var rules = JsonNode.Parse(File.ReadAllText(path))!["tools"]!["approvals"]!.AsObject();
        Assert.False(rules.ContainsKey("Server/Tool"));
        Assert.Equal("ask", rules["server/tool"]!.GetValue<string>());
        Assert.Equal(42, rules["future"]!.GetValue<int>());
    }
    [Theory]
    [InlineData("settings")]
    [InlineData("connection")]
    public void Editing_a_continue_in_target_preserves_unknown_keys_inside_the_array(string family)
    {
        var path = family == "settings" ? Path.Combine(_dir, "settings.json") : Path.Combine(_dir, "connections", "fixture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", family, "v1", "unknown-keys.json"), path);
        var original = JsonNode.Parse(File.ReadAllText(path))!;
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        var targets = family == "settings" ? settings.Chat.ContinueIn! : settings.Chat.Backends[0].ContinueIn!;
        targets[0] = targets[0] with { Name = "Edited target" };
        store.Save(settings);
        var edited = JsonNode.Parse(File.ReadAllText(path))!;
        var before = family == "settings" ? original["chat"]!["continueIn"]![0]! : original["continueIn"]![0]!;
        var after = family == "settings" ? edited["chat"]!["continueIn"]![0]! : edited["continueIn"]![0]!;
        Assert.True(JsonNode.DeepEquals(before["futureTargetProp"], after["futureTargetProp"]));
        Assert.Equal("Edited target", after["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{\"VERSION\":99,\"window\":{\"theme\":\"dark\"}}")]
    [InlineData("{\"version\":1.5}")]
    [InlineData("{\"version\":100000000000000000000}")]
    public void Every_higher_disk_version_is_protected(string original)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, original);
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        settings.Window.Theme = ThemeChoice.Light;
        store.Save(settings);
        Assert.NotEmpty(store.WriteProblems);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.False(Directory.Exists(Path.Combine(_dir, "backups")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unedited_invalid_connection_numbers_survive_an_unrelated_save(bool changeName)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "connections"));
        var path = Path.Combine(_dir, "connections", "test.json");
        const string original = """{"version":1,"type":"local","temperature":99,"maxTokens":0}""";
        File.WriteAllText(path, original);
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        if (changeName) settings.Chat.Backends[0].Name = "Edited";
        store.Save(settings);
        var result = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal(99, result["temperature"]!.GetValue<int>());
        Assert.Equal(0, result["maxTokens"]!.GetValue<int>());
        if (!changeName) Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Model_defaults_equal_the_embedded_settings_document()
    {
        using var stream = typeof(HotlineSettings).Assembly.GetManifestResourceStream("Hotline.Core.Defaults.settings.json")!;
        using var reader = new StreamReader(stream);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(reader.ReadToEnd()), JsonNode.Parse(SettingsStore.Serialize(new HotlineSettings()))));
        Assert.DoesNotContain("$schema", SettingsStore.Serialize(new HotlineSettings()));
    }

    [Theory]
    [InlineData("con", false)]
    [InlineData("COM1.port", false)]
    [InlineData("a b", false)]
    [InlineData("a/b", false)]
    [InlineData("..", false)]
    [InlineData("trailing.", false)]
    [InlineData("My-Connection_2.1", true)]
    public void Connection_ids_are_refused_instead_of_sanitized(string id, bool valid)
    {
        Assert.Equal(valid, SettingsStore.ValidConnectionId(id));
        Assert.False(SettingsStore.ValidConnectionId(new string('a', 65)));
        Assert.True(SettingsStore.ValidConnectionId(new string('a', 64)));
        Assert.Equal("connection:my-id:apiKey", Backends.SecretKeys.ApiKey("My-ID"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_backup_prevents_settings_or_connection_replacement(bool connection)
    {
        var files = new FakeFileSystem();
        var path = connection ? Path.Combine(_dir, "connections", "test.json") : Path.Combine(_dir, "settings.json");
        files.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = Encoding.UTF8.GetBytes(connection ? """{"version":1,"type":"local"}""" : """{"version":1}""");
        files.Files[path] = original;
        var store = new SettingsStore(_dir, files);
        var settings = store.Load();
        if (connection) settings.Chat.Backends[0].Name = "changed";
        else settings.General.SupportReminder = false;
        files.Fail = c => c.Operation == nameof(IFileSystem.CopyFile);
        store.Save(settings);
        Assert.NotEmpty(store.WriteProblems);
        Assert.Equal(original, files.Files[path]);
        Assert.DoesNotContain(files.Writes, c => c.Operation == nameof(IFileSystem.WriteDurable));
    }

    [Fact]
    public void Own_last_write_hash_skips_backups_across_store_instances_but_hand_edits_do_not()
    {
        var files = new FakeFileSystem();
        var path = Path.Combine(_dir, "settings.json");
        files.Files[path] = Encoding.UTF8.GetBytes("""{"version":1,"future":42}""");
        var state = new StateStore(Path.Combine(_dir, "state.json"));
        for (var i = 0; i < 3; i++)
        {
            if (i == 2) files.Files[path] = [.. files.Files[path], (byte)' '];
            var store = new SettingsStore(_dir, files, state);
            var settings = store.Load();
            settings.Window.FontSize = 15 + i;
            store.Save(settings);
            Assert.Empty(store.WriteProblems);
            Assert.Equal(SafeFile.Hash(files.Files[path]), state.GetLastWritten("settings.json"));
        }
        Assert.Equal(2, files.Writes.Count(c => c.Operation == nameof(IFileSystem.CopyFile)));
    }

    [Theory]
    [InlineData(nameof(IFileSystem.EnumerateFiles))]
    [InlineData(nameof(IFileSystem.DeleteFile))]
    public void Connection_file_system_failures_preserve_files_and_are_reported(string operation)
    {
        var files = new FakeFileSystem();
        var path = Path.Combine(_dir, "connections", "test.json");
        files.CreateDirectory(Path.GetDirectoryName(path)!);
        files.Files[path] = Encoding.UTF8.GetBytes("""{"version":1,"type":"local"}""");
        files.Files[Path.Combine(_dir, "settings.json")] = Encoding.UTF8.GetBytes("""{"version":1}""");
        var store = new SettingsStore(_dir, files);
        var settings = store.Load();
        files.Fail = c => c.Operation == operation;
        if (operation == nameof(IFileSystem.EnumerateFiles)) settings = store.Load();
        else settings.Chat.Backends.Clear();
        store.Save(settings);
        Assert.NotEmpty(store.SettingsProblems);
        Assert.True(files.FileExists(path));
        Assert.Contains(files.Calls, c => c.Operation == operation);
        if (operation == nameof(IFileSystem.DeleteFile)) Assert.Contains(files.Writes, c => c.Operation == operation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{broken")]
    [InlineData("{\"version\":1,\"general\":null,\"window\":{\"width\":null},\"chat\":{\"saveHistory\":null}}")]
    [InlineData("{\"schemaVersion\":15}")]
    public void Load_never_writes_even_when_files_are_missing_or_broken(string? text)
    {
        var files = new FakeFileSystem();
        var path = Path.Combine(_dir, "settings.json");
        if (text is not null) files.Files[path] = Encoding.UTF8.GetBytes(text);
        files.Fail = call => files.Writes.Contains(call);
        var store = new SettingsStore(_dir, files);
        var loaded = store.Load();
        Assert.Equal("40%", loaded.Window.Width.ToString());
        Assert.True(loaded.Chat.SaveHistory);
        store.TryRead();
        Assert.Empty(files.Writes);
        Assert.False(File.Exists(Path.Combine(_dir, "state.json")));
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("connection")]
    public void Editing_a_known_field_preserves_every_other_key_and_order(string family)
    {
        var path = family == "settings" ? Path.Combine(_dir, "settings.json") : Path.Combine(_dir, "connections", "fixture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", family, "v1", "unknown-keys.json"));
        File.WriteAllText(path, original);
        var expected = JsonNode.Parse(original)!.AsObject();
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        if (family == "settings")
        {
            settings.Window.Anchor.X = 0.25;
            expected["window"]!["anchor"]!["x"] = 0.25;
        }
        else
        {
            settings.Chat.Backends[0].Model = "changed";
            expected["model"] = "changed";
        }
        store.Save(settings);
        var actual = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.True(JsonNode.DeepEquals(expected, actual));
        Assert.Equal(expected.Select(p => p.Key), actual.AsObject().Select(p => p.Key));
        Assert.Equal(original, File.ReadAllText(Assert.Single(SettingsTestFiles.Backups(_dir, Path.GetFileName(path)))));
    }

    [Theory]
    [InlineData("{\"version\":1,\"Window\":{\"Theme\":\"dark\",\"future\":1}}")]
    [InlineData("{\"version\":1,\"window\":{\"theme\":\"dark\",\"theme\":\"light\",\"future\":1}}")]
    public void Edits_use_the_same_case_insensitive_last_member_rule_as_loading(string original)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, original);
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        settings.Window.Theme = ThemeChoice.HighContrast;
        store.Save(settings);
        Assert.Empty(store.WriteProblems);
        Assert.Equal(ThemeChoice.HighContrast, new SettingsStore(_dir).Load().Window.Theme);
        Assert.Contains("future", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("ALLOW")]
    [InlineData(" future word ")]
    public void Approval_words_are_preserved_without_policy_validation(string word)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, $$"""{"version":1,"tools":{"approvals":{"server/tool":"{{word}}","bad":42} } }""");
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        Assert.Equal(word, settings.Tools.Approvals["server/tool"]);
        Assert.Single(store.SettingsProblems);
        settings.Tools.Approvals["server/second"] = "new word";
        store.Save(settings);
        Assert.Equal(word, JsonNode.Parse(File.ReadAllText(path))!["tools"]!["approvals"]!["server/tool"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("settings", "default")]
    [InlineData("settings", "sparse")]
    [InlineData("settings", "unknown-keys")]
    [InlineData("settings", "newer-version")]
    [InlineData("connection", "default")]
    [InlineData("connection", "sparse")]
    [InlineData("connection", "unknown-keys")]
    [InlineData("connection", "newer-version")]
    public void Fixtures_load_and_save_unchanged_bytes(string family, string fixture)
    {
        var path = family == "settings" ? Path.Combine(_dir, "settings.json") : Path.Combine(_dir, "connections", "fixture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", family, "v1", fixture + ".json"));
        File.WriteAllBytes(path, original);
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        if (family == "connection") Assert.Single(settings.Chat.Backends);
        else Assert.Equal(fixture == "newer-version" ? 99 : 1, settings.SchemaVersion);
        store.Save(settings);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(Directory.Exists(Path.Combine(_dir, "backups")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":15,\"window\":{\"minWidth\":900}}")]
    public void Pre_freeze_load_uses_defaults_and_one_problem_without_writes(string original)
    {
        var path = Path.Combine(_dir, "settings.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, original);
        var store = new SettingsStore(_dir);
        Assert.Equal(320, store.Load().Window.MinWidth);
        Assert.Single(store.SettingsProblems);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_dir));
    }
}
