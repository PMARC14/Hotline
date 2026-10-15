using Hotline.Core.Backends;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Continue in targets: which list a chat gets, the defaults and the ready-made entries.</summary>
public sealed class ContinueTargetsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private static HotlineSettings With(params BackendProfile[] profiles)
    {
        var s = new HotlineSettings();
        s.Chat.Backends.Clear();
        s.Chat.Backends.AddRange(profiles);
        return s;
    }

    private static BackendProfile Local(string endpoint = "http://127.0.0.1:8080/v1") =>
        new() { Id = "local", Type = BackendType.Local, Name = "Local", Endpoint = endpoint };

    private static BackendProfile Api() => new() { Id = "claude", Type = BackendType.Anthropic, Name = "Claude" };

    [Fact]
    public void Shared_defaults_to_the_llama_cpp_web_ui_at_the_local_connection()
    {
        var target = Assert.Single(ContinueTargets.Shared(With(Api(), Local())));
        Assert.Equal(ContinueKind.Web, target.Kind);
        Assert.Equal("http://127.0.0.1:8080/?q={chat}&model={model}", target.Url); // the origin, not the /v1 API path
    }

    [Fact]
    public void No_local_connection_means_an_empty_default() => Assert.Empty(ContinueTargets.Shared(With(Api())));

    [Fact]
    public void A_set_shared_list_is_used_as_is()
    {
        var s = With(Local());
        s.Chat.ContinueIn = [new ContinueTarget(ContinueKind.App, "LM Studio", Command: "lms.exe")];
        Assert.Equal("LM Studio", Assert.Single(ContinueTargets.For(Api(), s)).Name);
    }

    [Fact]
    public void A_connection_list_replaces_the_shared_one()
    {
        var api = Api();
        api.ContinueIn = [new ContinueTarget(ContinueKind.Web, "Claude", Url: "https://claude.ai/new?q={chat}")];
        Assert.Equal("Claude", Assert.Single(ContinueTargets.For(api, With(api, Local()))).Name);
    }

    [Fact]
    public void An_empty_connection_list_means_none()
    {
        var api = Api();
        api.ContinueIn = [];
        Assert.Empty(ContinueTargets.For(api, With(api, Local())));
    }

    [Fact]
    public void Lists_round_trip_through_settings_and_connection_files()
    {
        var store = new SettingsStore(_dir);
        var s = store.Load();
        var api = Api();
        api.ContinueIn = [new ContinueTarget(ContinueKind.OpenWebUi, "Open WebUI", Url: "http://localhost:3000")];
        s.Chat.Backends.Add(api);
        s.Chat.ContinueIn = [new ContinueTarget(ContinueKind.App, "LM Studio", Command: "x.exe", CopyChat: true)];
        store.Save(s);

        Assert.Contains("\"openWebUi\"", File.ReadAllText(Path.Combine(_dir, "connections", "claude.json")));
        Assert.Contains("\"app\"", File.ReadAllText(Path.Combine(_dir, SettingsStore.FileName)));
        var back = new SettingsStore(_dir).Load();
        Assert.True(Assert.Single(back.Chat.ContinueIn!).CopyChat);
        Assert.Equal(ContinueKind.OpenWebUi, Assert.Single(back.Chat.Backends.Single(b => b.Id == "claude").ContinueIn!).Kind);
    }

    [Fact]
    public void Ready_made_entries_match_the_spec()
    {
        var r = ContinueTargets.ReadyMade.ToDictionary(e => e.Key, e => e.Target);
        Assert.Equal("http://127.0.0.1:8080/?q={chat}&model={model}", r["llamacpp"].Url);
        Assert.Equal((ContinueKind.OpenWebUi, "http://localhost:3000"), (r["openwebui"].Kind, r["openwebui"].Url));
        Assert.Equal((ContinueKind.App, @"%LOCALAPPDATA%\Programs\LM Studio\LM Studio.exe", true),
            (r["lmstudio"].Kind, r["lmstudio"].Command, r["lmstudio"].CopyChat));
        Assert.Equal("https://chatgpt.com/?q={chat}", r["chatgpt"].Url);
        Assert.Equal("https://claude.ai/new?q={chat}", r["claude"].Url);
    }
}
