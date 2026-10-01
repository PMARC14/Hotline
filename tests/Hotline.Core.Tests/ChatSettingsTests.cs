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
    public void Defaults_to_agy_backend()
    {
        var s = new SettingsStore(_dir).Load();
        Assert.Equal("agy", s.Chat.DefaultBackend);
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
        => Assert.Equal("agy", LoadJson("""{ "chat": null }""").Chat.DefaultBackend);
}
