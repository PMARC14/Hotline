using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Backends;
using Hotline.Core.Files;
using Hotline.Core.Settings;
using Hotline.Core.State;
using Hotline.Core.Tests.Files;
using Hotline.Core.Upgrade;
using Json.Schema;

namespace Hotline.Core.Tests.Upgrade;

public sealed class UpgradeTests : TempDirTest
{
    private StateStore State => new(Path.Combine(_dir, StateStore.FileName));
    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeFileSystem _files = new();
    private string PathOf(string name) => Path.Combine(_dir, name.Replace('/', Path.DirectorySeparatorChar));
    private void Put(string name, string text)
    {
        var path = PathOf(name);
        _files.CreateDirectory(Path.GetDirectoryName(path)!);
        _files.Files[path] = Encoding.UTF8.GetBytes(text);
    }
    private JsonObject Read(string name) => JsonNode.Parse(_files.Files[PathOf(name)])!.AsObject();

    [Fact]
    public void Pre_freeze_fixture_becomes_schema_valid_and_second_run_writes_nothing()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "pre-freeze");
        foreach (var file in Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories))
            Put(Path.GetRelativePath(fixture, file), File.ReadAllText(file));
        var originals = _files.Files.ToDictionary(p => p.Key, p => p.Value);
        _files.Calls.Clear();
        var result = PreFreezeUpgrade.Run(_dir, _files, State, _secrets);
        Assert.Empty(result.Failed);
        Assert.NotNull(result.Backup);
        foreach (var (path, bytes) in originals)
            Assert.Equal(bytes, _files.Files[Path.Combine(_dir, result.Backup, Path.GetRelativePath(_dir, path))]);
        var firstWrite = _files.Calls.FindIndex(c => c.Operation == "WriteDurable");
        Assert.Equal(originals.Count, _files.Calls.Take(firstWrite).Count(c => c.Operation == "CopyFile"));
        Assert.Equal("claude-work", Read("settings.json")["chat"]!["defaultConnection"]!.GetValue<string>());
        Assert.Equal(new[] { "claude-work", "local" }, Read("settings.json")["chat"]!["order"]!.AsArray().Select(x => x!.GetValue<string>()));
        foreach (var (name, family, version) in new[] { ("settings.json", "settings", 1), ("toolbar.json", "toolbar", 3),
                     ("mcp.json", "mcp", 1), ("connections/claude-work.json", "connection", 1), ("connections/local.json", "connection", 1) })
        {
            var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schemas", $"{family}.v{version}.schema.json")),
                new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            using var doc = JsonDocument.Parse(_files.Files[PathOf(name)]);
            Assert.True(schema.Evaluate(doc.RootElement, new EvaluationOptions { RequireFormatValidation = true }).IsValid, name);
        }
        Assert.True(State.IsShown(ShownFlag.FirstRun));
        Assert.True(State.IsShown(ShownFlag.SupportNote));
        var stateSchema = JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schemas", "state.v1.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        using var stateDoc = JsonDocument.Parse(File.ReadAllBytes(State.FilePath));
        Assert.True(stateSchema.Evaluate(stateDoc.RootElement, new EvaluationOptions { RequireFormatValidation = true }).IsValid);
        Assert.False(_files.Files.ContainsKey(PathOf(".first-run-shown")));
        var stateBefore = File.ReadAllBytes(State.FilePath);
        _files.Writes.Clear();
        var again = PreFreezeUpgrade.Run(_dir, _files, State, _secrets);
        Assert.False(again.Changed);
        Assert.Null(again.Backup);
        Assert.Empty(_files.Writes);
        Assert.Equal(stateBefore, File.ReadAllBytes(State.FilePath));
        Assert.Single(_files.Directories, p => Path.GetFileName(p).EndsWith("-upgrade"));
    }

    [Theory]
    [InlineData("CopyFile", true)]
    [InlineData("WriteDurable", false)]
    [InlineData("ReadAllBytes", false)]
    public void Upgrade_continues_without_backup_but_failed_files_retry(string operation, bool upgraded)
    {
        Put("settings.json", """{"chat":{"defaultBackend":"old"}}""");
        _files.Fail = c => c.Operation == operation && c.Path == PathOf("settings.json");
        var result = PreFreezeUpgrade.Run(_dir, _files, State, _secrets);
        Assert.Equal(upgraded, Read("settings.json").ContainsKey("version"));
        if (upgraded) Assert.Null(State.GetUpgrade()!.Backup);
        else Assert.Contains("settings.json", result.Failed);
        _files.Fail = null;
        var retry = PreFreezeUpgrade.Run(_dir, _files, State, _secrets);
        Assert.Equal(!upgraded, retry.Changed);
        Assert.Equal(1, Read("settings.json")["version"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("bad id", "bad_id")]
    [InlineData("Mixed", "Mixed")]
    [InlineData("..", "connection")]
    public void Connection_renames_references_and_copies_case_sensitive_Locker_keys(string oldId, string expected)
    {
        Put("connections/source.json", new JsonObject { ["id"] = oldId, ["type"] = "openAi", ["extraArgs"] = "--name \"two words\"" }.ToJsonString());
        Put("settings.json", new JsonObject { ["chat"] = new JsonObject { ["defaultBackend"] = oldId, ["order"] = new JsonArray(oldId) } }.ToJsonString());
        var oldKey = $"connection:{oldId}:apiKey";
        _secrets.Set(oldKey, "test-secret");
        var result = PreFreezeUpgrade.Run(_dir, _files, State, _secrets);
        Assert.Empty(result.Failed);
        Assert.Equal("test-secret", _secrets.Get(SecretKeys.ApiKey(expected)));
        if (!oldKey.Equals(SecretKeys.ApiKey(expected), StringComparison.OrdinalIgnoreCase)) Assert.Null(_secrets.Get(oldKey));
        Assert.Equal(expected, Read("settings.json")["chat"]!["defaultConnection"]!.GetValue<string>());
        Assert.Equal(expected, Read("settings.json")["chat"]!["order"]![0]!.GetValue<string>());
        Assert.Equal(new[] { "--name", "two words" }, Read($"connections/{expected}.json")["extraArgs"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public void Connections_number_collisions_and_current_files_keep_unknown_keys()
    {
        Put("connections/keep.json", """{"version":1,"type":"local","extension":1}""");
        Put("connections/a.json", """{"id":"keep","type":"local","extension":2}""");
        Put("connections/b.json", """{"id":"keep","type":"local","extension":3}""");
        PreFreezeUpgrade.Run(_dir, _files, State, _secrets);
        Assert.Equal(1, Read("connections/keep.json")["extension"]!.GetValue<int>());
        Assert.Equal(2, Read("connections/keep-2.json")["extension"]!.GetValue<int>());
        Assert.Equal(3, Read("connections/keep-3.json")["extension"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("settings.json", "WriteDurable")]
    [InlineData("connections/source.json", "ReadAllBytes")]
    [InlineData("connections/source.json", "DeleteFile")]
    public void Partial_upgrade_retries_keep_selection_aliases_and_never_duplicate_a_renamed_connection(string blocked, string operation)
    {
        Put("settings.json", """{"chat":{"defaultBackend":"bad id","order":["source"]},"general":{"language":"fr"}}""");
        Put("connections/source.json", """{"id":"bad id","type":"local"}""");
        Put("toolbar.json", """{"version":2}""");
        _files.Fail = c => c.Path == PathOf(blocked) && c.Operation == operation;
        var first = PreFreezeUpgrade.Run(_dir, _files, State, _secrets);
        Assert.Contains(blocked, first.Failed);
        Assert.True(_files.Files.ContainsKey(PathOf("connections/source.json")));
        Assert.Equal(3, Read("toolbar.json")["version"]!.GetValue<int>());
        _files.Fail = null;
        var second = PreFreezeUpgrade.Run(_dir, _files, State, _secrets);
        Assert.Empty(second.Failed);
        Assert.False(_files.Files.ContainsKey(PathOf("connections/source.json")));
        Assert.True(_files.Files.ContainsKey(PathOf("connections/bad_id.json")));
        Assert.Equal("bad_id", Read("settings.json")["chat"]!["defaultConnection"]!.GetValue<string>());
        Assert.Equal("bad_id", Read("settings.json")["chat"]!["order"]![0]!.GetValue<string>());
        Assert.Equal("system", Read("settings.json")["general"]!["language"]!.GetValue<string>());
        Assert.Single(_files.Files.Keys, p => Path.GetDirectoryName(p) == PathOf("connections"));
    }

    private sealed class UnreliableSecrets : ISecretStore
    {
        internal readonly InMemorySecretStore Values = new();
        internal bool IgnoreCopies = true;
        public string? Get(string key) => Values.Get(key);
        public void Set(string key, string? value) { if (!IgnoreCopies) Values.Set(key, value); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Locker_copy_must_read_back_before_old_key_is_deleted_and_failures_retry(bool current)
    {
        Put("connections/Mixed.json", current ? """{"version":1,"type":"local"}""" : """{"id":"Mixed","type":"local"}""");
        var secrets = new UnreliableSecrets();
        secrets.Values.Set("connection:Mixed:apiKey", "test-secret");
        var first = PreFreezeUpgrade.Run(_dir, _files, State, secrets);
        Assert.Contains("connections/Mixed.json", first.Failed);
        Assert.Equal("test-secret", secrets.Get("connection:Mixed:apiKey"));
        if (!current) Assert.True(Read("connections/Mixed.json").ContainsKey("id"));
        secrets.IgnoreCopies = false;
        Assert.Empty(PreFreezeUpgrade.Run(_dir, _files, State, secrets).Failed);
        Assert.Equal("test-secret", secrets.Get("connection:mixed:apiKey"));
        // Kept: in the real Locker a name differing only by case is the same entry.
        Assert.Equal("test-secret", secrets.Get("connection:Mixed:apiKey"));
        _files.Writes.Clear();
        Assert.False(PreFreezeUpgrade.Run(_dir, _files, State, secrets).Changed);
        Assert.Empty(_files.Writes);
        Assert.Equal("test-secret", secrets.Get("connection:mixed:apiKey"));
    }

    /// <summary>Credential Locker matches names without case, like Windows Credential Manager.</summary>
    private sealed class CaseInsensitiveSecrets : ISecretStore
    {
        internal readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);
        internal bool FailReads;
        public string? Get(string key) => TryGet(key, out var value) ? value : null;
        public bool TryGet(string key, out string? value)
        {
            value = FailReads ? null : Values.GetValueOrDefault(key);
            return !FailReads;
        }
        public void Set(string key, string? value)
        {
            if (string.IsNullOrEmpty(value)) Values.Remove(key);
            else Values[key] = value;
        }
    }

    [Theory]
    [InlineData("""{"version":1,"type":"local"}""")]
    [InlineData("""{"id":"Mixed","type":"local"}""")]
    public void A_case_insensitive_Locker_never_loses_a_key_whose_name_differs_only_by_case(string file)
    {
        Put("connections/Mixed.json", file);
        var secrets = new CaseInsensitiveSecrets();
        secrets.Values["connection:Mixed:apiKey"] = "test-secret";
        Assert.Empty(PreFreezeUpgrade.Run(_dir, _files, State, secrets).Failed);
        Assert.Equal("test-secret", secrets.Get("connection:mixed:apiKey"));
    }

    [Fact]
    public void Connections_sharing_an_old_id_both_keep_its_key()
    {
        Put("connections/a.json", """{"id":"bad id","type":"local","n":1}""");
        Put("connections/b.json", """{"id":"bad id","type":"local","n":2}""");
        _secrets.Set("connection:bad id:apiKey", "shared-secret");
        Assert.Empty(PreFreezeUpgrade.Run(_dir, _files, State, _secrets).Failed);
        Assert.Equal("shared-secret", _secrets.Get("connection:bad_id:apiKey"));
        Assert.Equal("shared-secret", _secrets.Get("connection:bad_id-2:apiKey"));
        Assert.Null(_secrets.Get("connection:bad id:apiKey"));
    }

    [Fact]
    public void A_Locker_read_failure_leaves_the_connection_for_the_next_start()
    {
        Put("connections/source.json", """{"id":"bad id","type":"local"}""");
        var secrets = new CaseInsensitiveSecrets { FailReads = true };
        secrets.Values["connection:bad id:apiKey"] = "test-secret";
        Assert.Contains("connections/source.json", PreFreezeUpgrade.Run(_dir, _files, State, secrets).Failed);
        Assert.True(Read("connections/source.json").ContainsKey("id"));
        Assert.False(_files.Files.ContainsKey(PathOf("connections/bad_id.json")));
        secrets.FailReads = false;
        Assert.Empty(PreFreezeUpgrade.Run(_dir, _files, State, secrets).Failed);
        Assert.Equal("test-secret", secrets.Get("connection:bad_id:apiKey"));
    }

    [Fact]
    public void A_rename_interrupted_after_writing_the_new_file_resumes_without_a_duplicate()
    {
        Put("connections/source.json", """{"id":"bad id","type":"local","extraArgs":"--a b"}""");
        Put("settings.json", """{"chat":{"defaultBackend":"bad id"}}""");
        _secrets.Set("connection:bad id:apiKey", "test-secret");
        // Writing bad_id.json works but no delete does: the same state as a crash between the two.
        _files.Fail = c => c.Operation == "DeleteFile";
        Assert.Contains("connections/source.json", PreFreezeUpgrade.Run(_dir, _files, State, _secrets).Failed);
        _files.Fail = null;
        Assert.Empty(PreFreezeUpgrade.Run(_dir, _files, State, _secrets).Failed);
        Assert.False(_files.Files.ContainsKey(PathOf("connections/source.json")));
        Assert.False(_files.Files.ContainsKey(PathOf("connections/bad_id-2.json")));
        Assert.Equal("bad_id", Read("settings.json")["chat"]!["defaultConnection"]!.GetValue<string>());
        Assert.Equal("test-secret", _secrets.Get("connection:bad_id:apiKey"));
    }

    [Fact]
    public void A_retry_keeps_the_record_that_an_earlier_run_had_no_backup()
    {
        Put("settings.json", """{"chat":{}}""");
        Put("connections/source.json", """{"id":"bad id","type":"local"}""");
        _files.Fail = c => (c.Operation == "CopyFile" && c.Path == PathOf("settings.json"))
            || (c.Operation == "WriteDurable" && c.Path == PathOf("connections/bad_id.json"));
        var first = PreFreezeUpgrade.Run(_dir, _files, State, _secrets);
        Assert.Null(first.Backup);
        _files.Fail = null;
        var second = PreFreezeUpgrade.Run(_dir, _files, State, _secrets);
        Assert.True(second.Changed);
        Assert.Null(second.Backup);
        Assert.Null(State.GetUpgrade()!.Backup);
    }

    [Fact]
    public void Current_and_newer_files_are_unchanged_and_long_ids_still_fit_with_collision_suffixes()
    {
        foreach (var name in new[] { "settings.json", "toolbar.json", "mcp.json", "connections/newer.json" })
            Put(name, """{"version":99,"id":"old","approvals":{},"extension":true}""");
        var before = _files.Files.ToDictionary(p => p.Key, p => p.Value);
        _files.Writes.Clear();
        Assert.False(PreFreezeUpgrade.Run(_dir, _files, State, _secrets).Changed);
        Assert.Empty(_files.Writes);
        foreach (var (path, bytes) in before) Assert.Equal(bytes, _files.Files[path]);
        _files.Files.Remove(PathOf("settings.json"));
        var id = new string('a', 80);
        Put("connections/first.json", new JsonObject { ["id"] = id, ["type"] = "local" }.ToJsonString());
        Put("connections/second.json", new JsonObject { ["id"] = id, ["type"] = "local" }.ToJsonString());
        Assert.Empty(PreFreezeUpgrade.Run(_dir, _files, State, _secrets).Failed);
        Assert.True(_files.Files.ContainsKey(PathOf($"connections/{new string('a', 64)}.json")));
        Assert.True(_files.Files.ContainsKey(PathOf($"connections/{new string('a', 62)}-2.json")));
    }

    [Fact]
    public void Renamed_connections_find_old_lowercase_keys_and_keep_keys_still_used_by_another_connection()
    {
        Put("connections/source.json", """{"id":"Mixed Name","type":"local"}""");
        Put("connections/keep.json", """{"version":1,"type":"local"}""");
        Put("connections/collision.json", """{"id":"keep","type":"local"}""");
        _secrets.Set("connection:mixed name:apiKey", "mixed-secret");
        _secrets.Set("connection:keep:apiKey", "shared-secret");
        Assert.Empty(PreFreezeUpgrade.Run(_dir, _files, State, _secrets).Failed);
        Assert.Equal("mixed-secret", _secrets.Get("connection:mixed_name:apiKey"));
        Assert.Null(_secrets.Get("connection:mixed name:apiKey"));
        Assert.Equal("shared-secret", _secrets.Get("connection:keep-2:apiKey"));
        Assert.Equal("shared-secret", _secrets.Get("connection:keep:apiKey"));
    }

    [Fact]
    public void Retrying_a_collision_keeps_the_already_converted_default_selected()
    {
        Put("settings.json", """{"chat":{"defaultBackend":"same","order":["a","b"]}}""");
        Put("connections/a.json", """{"id":"same","type":"local"}""");
        Put("connections/b.json", """{"id":"same","type":"local"}""");
        _files.Fail = c => c.Operation == "WriteDurable" && c.Path == PathOf("connections/same-2.json");
        Assert.Contains("connections/same-2.json", PreFreezeUpgrade.Run(_dir, _files, State, _secrets).Failed);
        _files.Fail = null;
        Assert.Empty(PreFreezeUpgrade.Run(_dir, _files, State, _secrets).Failed);
        Assert.Equal("same", Read("settings.json")["chat"]!["defaultConnection"]!.GetValue<string>());
        Assert.Equal(new[] { "same", "same-2" }, Read("settings.json")["chat"]!["order"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void Starters_preserve_existing_files_refresh_defaults_and_are_idempotent()
    {
        Put("settings.json", """{"version":99,"extension":true}""");
        Put(".defaults/settings.json", "stale");
        Assert.Empty(StarterFiles.Ensure(_dir, _files, State, _secrets));
        Assert.Equal(99, Read("settings.json")["version"]!.GetValue<int>());
        foreach (var family in new[] { "settings", "toolbar", "mcp" })
            Assert.Equal(StarterFiles.DefaultBytes(family), _files.Files[PathOf($".defaults/{family}.json")]);
        Assert.Equal(StarterFiles.DefaultBytes("toolbar"), _files.Files[PathOf("toolbar.json")]);
        _files.Writes.Clear();
        StarterFiles.Ensure(_dir, _files, State, _secrets);
        Assert.Empty(_files.Writes);
    }
}
