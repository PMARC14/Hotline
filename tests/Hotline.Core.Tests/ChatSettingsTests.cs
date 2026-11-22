using Hotline.Core.Settings;
using Hotline.Core.Backends;

namespace Hotline.Core.Tests;

public sealed class ChatSettingsTests : TempDirTest
{

    private HotlineSettings LoadJson(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), json);
        return new SettingsStore(_dir).Load();
    }

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

    [Fact]
    public void New_install_has_no_connection_until_one_is_chosen()
    {
        var s = new SettingsStore(_dir).Load();
        Assert.Equal("", s.Chat.DefaultBackend); // the panel asks on first open
        Assert.Empty(s.Chat.Connections);
        Assert.Empty(new SettingsStore(_dir).Load().Chat.Connections); // and nothing is made up on the next start
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
        Assert.Empty(back.Chat.Connections);
        Assert.Equal("", back.Chat.DefaultBackend);
        Assert.Empty(new SettingsStore(_dir).TryRead()!.Chat.Connections);
    }

    [Fact]
    public void Older_settings_without_connection_files_get_no_connection_added()
        => Assert.Empty(LoadJson("""{ "version": 1, "chat": { "defaultConnection": "agy" } }""").Chat.Connections);

    [Theory]
    [InlineData("""{ "version": 1, "chat": { "defaultConnection": "nope", "backends": [ { "id": "g", "type": "gemini" } ] } }""", "nope")] // preserved even before its connection is installed
    [InlineData("""{ "version": CURRENT, "chat": { "defaultConnection": "nope" } }""", "nope")]                          // unknown, none to fall back to
    [InlineData("""{ "version": 1, "chat": { "defaultConnection": "  " } }""", "  ")]                                 // blank stays unchosen
    public void Default_backend_is_the_first_connection_or_unchosen(string json, string expected)
        => Assert.Equal(expected, LoadJson(json.Replace("CURRENT", HotlineSettings.CurrentSchemaVersion.ToString())).Chat.DefaultBackend);



    [Fact]
    public void Null_chat_section_gets_defaults()
        => Assert.Equal(300, LoadJson("""{ "version": 1, "chat": null }""").Chat.HistoryListSize);
}
