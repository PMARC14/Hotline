using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Each connection lives in ~/.hotline/connections/&lt;id&gt;.json; settings.json holds order and default.</summary>
public sealed class ConnectionFilesTests : TempDirTest
{
    private readonly SettingsFileSystem _files = new();
    private string Conn(string id) => Path.Combine(_dir, "connections", id + ".json");
    private string SettingsFile => Path.Combine(_dir, SettingsStore.FileName);
    private void WithAgy()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "connections"));
        File.WriteAllText(Conn("agy"), """{"version":1,"type":"antigravity","name":"Gemini (Antigravity)","agent":"hotline"}""");
        File.WriteAllText(SettingsFile, """{"version":1}""");
    }

    [Fact]
    public void Fresh_install_writes_no_connection_and_no_backends_in_settings()
    {
        var s = new SettingsStore(_dir, _files).Load();
        Assert.False(Directory.Exists(Path.Combine(_dir, "connections")) && Directory.EnumerateFiles(Path.Combine(_dir, "connections")).Any());
        Assert.False(File.Exists(SettingsFile));
        Assert.False(Directory.Exists(_dir));
        Assert.Empty(s.Chat.Connections);
    }




    [Fact]
    public void Order_comes_from_settings_and_unlisted_files_follow_by_name()
    {
        WithAgy();
        var store = new SettingsStore(_dir, _files);
        store.Save(store.Load());
        Directory.CreateDirectory(store.ConnectionsDirectory);
        File.WriteAllText(Conn("zeta"), """{ "version": 1, "type": "local", "name": "Zeta" }""");
        File.WriteAllText(Conn("alpha"), """{ "version": 1, "type": "local", "name": "Alpha" }""");
        var root = System.Text.Json.Nodes.JsonNode.Parse(SettingsStore.Serialize(store.Load()))!;
        root["chat"]!["order"] = new System.Text.Json.Nodes.JsonArray("zeta", "agy");
        File.WriteAllText(SettingsFile, root.ToJsonString());
        var s = new SettingsStore(_dir, _files).Load();
        Assert.Equal(["zeta", "agy", "alpha"], s.Chat.Connections.Select(b => b.Id)); // id from the file name
    }

    [Fact]
    public void Removing_a_hand_written_connection_keeps_its_exact_bytes_in_a_backup()
    {
        WithAgy();
        var store = new SettingsStore(_dir, _files);
        var settings = store.Load();
        var original = File.ReadAllBytes(Conn("agy"));
        settings.Chat.Connections.Clear();
        store.Save(settings, ["agy"]);
        Assert.False(File.Exists(Conn("agy")));
        Assert.Equal(original, File.ReadAllBytes(Assert.Single(SettingsTestFiles.Backups(_dir, "agy.json"))));
        Assert.DoesNotContain("agy", store.KnownConnectionIds);
        Assert.Empty(store.WriteProblems);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_removal_keeps_the_connection_file_and_ownership(bool backupFails)
    {
        WithAgy();
        var store = new SettingsStore(_dir, _files);
        var settings = store.Load();
        var path = Conn("agy");
        var original = File.ReadAllBytes(path);
        var blocked = Path.Combine(_dir, "blocked");
        File.WriteAllText(blocked, "not a directory");
        if (backupFails) _files.BeforeCopy = file => File.Copy(file, Path.Combine(blocked, "backup"));
        else File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            settings.Chat.Connections.Clear();
            store.Save(settings, ["agy"]);
            Assert.NotEmpty(store.WriteProblems);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(Path.GetFullPath(path), store.SourcePath("agy"));
            Assert.Contains("agy", store.KnownConnectionIds);
        }
        finally { if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal); }
        _files.BeforeCopy = null;
        store.Save(settings, ["agy"]); // ownership survived: a retry can finish removing it
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Removing_a_connection_deletes_its_file()
    {
        WithAgy();
        var store = new SettingsStore(_dir, _files);
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
        var store = new SettingsStore(_dir, _files);
        store.Save(store.Load());
        Directory.CreateDirectory(store.ConnectionsDirectory);
        File.WriteAllText(Conn("agy"), "{ broken");
        var s2 = new SettingsStore(_dir, _files);
        var loaded = s2.Load();
        Assert.Single(s2.BrokenConnectionFiles);
        s2.Save(loaded);
        Assert.Equal("{ broken", File.ReadAllText(Conn("agy")));
    }

    [Fact]
    public void A_file_broken_while_running_survives_a_live_read_and_the_next_save()
    {
        WithAgy();
        var store = new SettingsStore(_dir, _files);
        store.Load(); // agy.json was fine at start
        File.WriteAllText(Conn("agy"), "{ half-saved"); // the user's editor, mid-save
        var live = store.TryRead()!; // a live reload drops the unreadable connection...
        store.Save(live);            // ...and the next save must not delete its file
        Assert.Equal("{ half-saved", File.ReadAllText(Conn("agy")));
    }

    [Fact]
    public void A_broken_file_fixed_while_running_saves_normally_again()
    {
        var store = new SettingsStore(_dir, _files);
        store.Save(store.Load());
        Directory.CreateDirectory(store.ConnectionsDirectory);
        File.WriteAllText(Conn("agy"), "{ broken");
        var s = new SettingsStore(_dir, _files); s.Load();
        File.WriteAllText(Conn("agy"), """{ "version": 1, "type": "antigravity", "name": "Fixed" }""");
        var live = s.TryRead()!;
        Assert.Empty(s.BrokenConnectionFiles);
        live.Chat.Connections.Single(b => b.Id == "agy").Name = "Renamed";
        s.Save(live);
        Assert.Contains("Renamed", File.ReadAllText(Conn("agy")));
    }

    [Fact]
    public void Live_read_sees_hand_added_files_without_writing_anything()
    {
        var store = new SettingsStore(_dir, _files);
        store.Save(store.Load());
        Directory.CreateDirectory(store.ConnectionsDirectory);
        File.WriteAllText(Conn("mine"), """{ "version": 1, "type": "claudeCode", "name": "Mine", "model": "opus" }""");
        var before = File.ReadAllText(SettingsFile);
        var read = store.TryRead()!;
        Assert.Contains(read.Chat.Connections, b => b.Id == "mine" && b.Model == "opus");
        Assert.Equal(before, File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void Connection_files_omit_unset_options()
    {
        var store = new SettingsStore(_dir, _files);
        var s = store.Load();
        var agy = Hotline.Core.Backends.ConnectionEditor.Add(s.Chat, BackendType.Antigravity);
        store.Save(s);
        var json = File.ReadAllText(Conn(agy.Id));
        Assert.DoesNotContain("\"endpoint\"", json);
        Assert.Contains("\"agent\": \"hotline\"", json);
    }

    // ---- a hand-edited settings.json with null list elements must not crash start-up or a live reload ----

    [Theory]
    [InlineData("""{ "version": 1, "window": { "minWidth": 800 }, "chat": { "order": [null] } }""")]
    [InlineData("""{ "version": 1, "window": { "minWidth": 800 }, "chat": { "order": [null, "", "  ", "a"] } }""")]
    [InlineData("""{ "version": 1, "window": { "minWidth": 800 }, "chat": { "backends": [null] } }""")]
    [InlineData("""{ "version": 1, "window": { "minWidth": 800 }, "chat": { "backends": [null, { "id": null }, { "id": "x", "name": "X" }] } }""")]
    public void Null_list_elements_do_not_crash_load_or_try_read(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsFile, json);
        var store = new SettingsStore(_dir, _files);
        var loaded = store.Load();
        Assert.Equal(800, loaded.Window.MinWidth);
        var again = store.TryRead();
        Assert.NotNull(again);
        Assert.Equal(800, again.Window.MinWidth);
    }
}
