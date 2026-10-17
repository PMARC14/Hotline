using Hotline.Core.Settings;
using Hotline.Core.Backends;

namespace Hotline.Core.Tests;

public sealed class HistorySettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void History_settings_default_on_and_round_trip_through_settings_json()
    {
        var store = new Settings.SettingsStore(_dir);
        var s = store.Load();
        Assert.Equal((300, true, true), (s.Chat.HistoryListSize, s.Chat.ResumeWhileTyping, s.Chat.CliTranscripts));
        s.Chat.HistoryListSize = 50;
        s.Chat.ResumeWhileTyping = false;
        s.Chat.CliTranscripts = false;
        store.Save(s);
        var json = File.ReadAllText(Path.Combine(_dir, Settings.SettingsStore.FileName));
        Assert.Contains("\"historyListSize\": 50", json);
        var back = new Settings.SettingsStore(_dir).Load();
        Assert.Equal((50, false, false), (back.Chat.HistoryListSize, back.Chat.ResumeWhileTyping, back.Chat.CliTranscripts));
    }
}

public sealed class ChatSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private HotlineSettings LoadJson(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), json);
        return new SettingsStore(_dir).Load();
    }

    [Fact]
    public void New_install_has_no_connection_until_one_is_chosen()
    {
        var s = new SettingsStore(_dir).Load();
        Assert.Equal("", s.Chat.DefaultBackend); // the panel asks on first open
        Assert.Empty(s.Chat.Backends);
        Assert.Empty(new SettingsStore(_dir).Load().Chat.Backends); // and nothing is made up on the next start
    }

    [Fact]
    public void Removing_every_connection_leaves_none()
    {
        var store = new SettingsStore(_dir);
        var s = store.Load();
        var agy = ConnectionEditor.Add(s.Chat, BackendType.Antigravity);
        ConnectionEditor.SetDefault(s.Chat, agy.Id);
        store.Save(s);
        Assert.True(ConnectionEditor.Remove(s.Chat, agy.Id));
        store.Save(s);
        var back = new SettingsStore(_dir).Load();
        Assert.Empty(back.Chat.Backends);
        Assert.Equal("", back.Chat.DefaultBackend);
        Assert.Empty(new SettingsStore(_dir).TryRead()!.Chat.Backends);
    }

    [Fact]
    public void Older_settings_without_connection_files_keep_the_agy_they_had()
        => Assert.Equal("agy", Assert.Single(LoadJson("""{ "schemaVersion": 11, "chat": { "defaultBackend": "agy" } }""").Chat.Backends).Id);

    [Fact]
    public void Unknown_default_backend_falls_back_to_first()
        => Assert.Equal("agy", LoadJson("""{ "schemaVersion": 11, "chat": { "defaultBackend": "nope" } }""").Chat.DefaultBackend);

    [Fact]
    public void Unknown_default_backend_with_no_connections_is_unchosen()
        => Assert.Equal("", LoadJson($$"""{ "schemaVersion": {{HotlineSettings.CurrentSchemaVersion}}, "chat": { "defaultBackend": "nope" } }""").Chat.DefaultBackend);

    [Fact]
    public void Backends_without_id_are_dropped_and_blank_names_use_id()
    {
        var s = LoadJson("""{ "chat": { "backends": [ { "id": "", "type": "gemini" }, { "id": "g", "type": "gemini", "name": "" } ] } }""");
        var g = Assert.Single(s.Chat.Backends);
        Assert.Equal("g", g.Name);
        Assert.Equal(BackendType.Gemini, g.Type);
    }

    [Fact]
    public void Null_chat_section_gets_defaults()
        => Assert.Equal(300, LoadJson("""{ "chat": null }""").Chat.HistoryListSize);

    [Fact]
    public void Older_settings_keep_a_connection_chosen()
        => Assert.Equal("agy", LoadJson("""{ "schemaVersion": 9, "chat": { "defaultBackend": "" } }""").Chat.DefaultBackend);

    [Fact]
    public void Blank_default_stays_unchosen_on_current_settings()
        => Assert.Equal("", LoadJson("""{ "schemaVersion": 10, "chat": { "defaultBackend": "  " } }""").Chat.DefaultBackend);
}
