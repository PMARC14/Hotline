using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

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
    public void New_install_offers_agy_but_has_no_connection_chosen_yet()
    {
        var s = new SettingsStore(_dir).Load();
        Assert.Equal("", s.Chat.DefaultBackend); // the panel asks on first open
        var agy = Assert.Single(s.Chat.Backends);
        Assert.Equal(BackendType.Antigravity, agy.Type);
        Assert.Equal("hotline", agy.Agent);
    }

    [Fact]
    public void Empty_backend_list_falls_back_to_defaults()
        => Assert.Equal("agy", Assert.Single(LoadJson("""{ "chat": { "backends": [] } }""").Chat.Backends).Id);

    [Fact]
    public void Unknown_default_backend_falls_back_to_first()
        => Assert.Equal("agy", LoadJson("""{ "chat": { "defaultBackend": "nope" } }""").Chat.DefaultBackend);

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
        => Assert.Equal("agy", Assert.Single(LoadJson("""{ "chat": null }""").Chat.Backends).Id);

    [Fact]
    public void Older_settings_keep_a_connection_chosen()
        => Assert.Equal("agy", LoadJson("""{ "schemaVersion": 9, "chat": { "defaultBackend": "" } }""").Chat.DefaultBackend);

    [Fact]
    public void Blank_default_stays_unchosen_on_current_settings()
        => Assert.Equal("", LoadJson("""{ "schemaVersion": 10, "chat": { "defaultBackend": "  " } }""").Chat.DefaultBackend);
}
