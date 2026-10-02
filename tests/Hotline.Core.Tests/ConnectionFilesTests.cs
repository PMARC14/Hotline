using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Each connection lives in ~/.hotline/connections/&lt;id&gt;.json; settings.json holds order and default.</summary>
public sealed class ConnectionFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private string Conn(string id) => Path.Combine(_dir, "connections", id + ".json");
    private string SettingsFile => Path.Combine(_dir, SettingsStore.FileName);

    [Fact]
    public void Fresh_install_writes_one_file_per_connection_and_no_backends_in_settings()
    {
        var s = new SettingsStore(_dir).Load();
        Assert.True(File.Exists(Conn("agy")));
        Assert.DoesNotContain("\"backends\"", File.ReadAllText(SettingsFile));
        Assert.Contains("\"order\"", File.ReadAllText(SettingsFile));
        Assert.Equal("agy", s.Chat.Backends[0].Id);
    }

    [Fact]
    public void Old_backends_list_moves_into_files_keeping_its_order()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsFile, """{ "schemaVersion": 7, "chat": { "defaultBackend": "b", "backends": [ { "id": "b", "type": "claudeCode", "name": "B" }, { "id": "a", "type": "antigravity", "name": "A" } ] } }""");
        var s = new SettingsStore(_dir).Load();
        Assert.Equal(["b", "a"], s.Chat.Backends.Select(b => b.Id));
        Assert.True(File.Exists(Conn("a")) && File.Exists(Conn("b")));
        Assert.DoesNotContain("\"backends\"", File.ReadAllText(SettingsFile));
        Assert.Equal(["b", "a"], new SettingsStore(_dir).Load().Chat.Backends.Select(b => b.Id)); // order survives
        Assert.Equal("b", s.Chat.DefaultBackend);
    }

    [Fact]
    public void Order_comes_from_settings_and_unlisted_files_follow_by_name()
    {
        var store = new SettingsStore(_dir);
        store.Load();
        File.WriteAllText(Conn("zeta"), """{ "type": "local", "name": "Zeta" }""");
        File.WriteAllText(Conn("alpha"), """{ "type": "local", "name": "Alpha" }""");
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(SettingsFile))!;
        root["chat"]!["order"] = new System.Text.Json.Nodes.JsonArray("zeta", "agy");
        File.WriteAllText(SettingsFile, root.ToJsonString());
        var s = new SettingsStore(_dir).Load();
        Assert.Equal(["zeta", "agy", "alpha"], s.Chat.Backends.Select(b => b.Id)); // id from the file name
    }

    [Fact]
    public void Removing_a_connection_deletes_its_file()
    {
        var store = new SettingsStore(_dir);
        var s = store.Load();
        Hotline.Core.Backends.ConnectionEditor.Add(s.Chat, BackendType.Local);
        store.Save(s);
        Assert.True(File.Exists(Conn("local-model")));
        Hotline.Core.Backends.ConnectionEditor.Remove(s.Chat, "local-model");
        store.Save(s);
        Assert.False(File.Exists(Conn("local-model")));
        Assert.True(File.Exists(Conn("agy")));
    }

    [Fact]
    public void A_broken_connection_file_is_never_overwritten_or_deleted()
    {
        var store = new SettingsStore(_dir);
        store.Load();
        File.WriteAllText(Conn("agy"), "{ broken");
        var s2 = new SettingsStore(_dir);
        var loaded = s2.Load();
        Assert.Single(s2.BrokenConnectionFiles);
        s2.Save(loaded);
        Assert.Equal("{ broken", File.ReadAllText(Conn("agy")));
    }

    [Fact]
    public void Live_read_sees_hand_added_files_without_writing_anything()
    {
        var store = new SettingsStore(_dir);
        store.Load();
        File.WriteAllText(Conn("mine"), """{ "type": "claudeCode", "name": "Mine", "model": "opus" }""");
        var before = File.ReadAllText(SettingsFile);
        var read = store.TryRead()!;
        Assert.Contains(read.Chat.Backends, b => b.Id == "mine" && b.Model == "opus");
        Assert.Equal(before, File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void Connection_files_omit_unset_options()
    {
        new SettingsStore(_dir).Load();
        var json = File.ReadAllText(Conn("agy"));
        Assert.DoesNotContain("\"endpoint\"", json);
        Assert.Contains("\"agent\": \"hotline\"", json);
    }
}
