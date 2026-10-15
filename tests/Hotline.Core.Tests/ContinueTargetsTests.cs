using Hotline.Core.Backends;
using Hotline.Core.Chat;
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

    // ---- filling (Task 2) ----------------------------------------------------------------------

    private static ChatMessage Msg(ChatRole role, string text) => new(Guid.NewGuid().ToString("N"), role, text, [], DateTimeOffset.Now);

    private static ContinueValues Values(params ChatMessage[] messages) =>
        ContinueTargets.Values("Title", messages, file: @"C:\Temp\chat.md", model: "llama3");

    [Fact]
    public void Chat_value_is_the_continue_prompt_and_the_transcript()
    {
        var v = Values(Msg(ChatRole.User, "hi"), Msg(ChatRole.Assistant, "hello"), Msg(ChatRole.User, "more"));
        Assert.StartsWith("Here is a conversation from another app. Continue it: reply to the last message.\n\n# Title", v.Chat);
        Assert.Contains("## Assistant", v.Chat);
        Assert.Equal("more", v.Last);
        Assert.Equal("llama3", v.Model);
    }

    [Fact]
    public void Values_are_url_encoded()
    {
        const string tricky = "a&b #c %d 😀\nline";
        var filled = ContinueTargets.FillUrl("https://x.test/?q={last}", Values(Msg(ChatRole.User, tricky)));
        Assert.NotNull(filled);
        Assert.Equal(tricky, Uri.UnescapeDataString(filled!.Uri.AbsoluteUri.Split("?q=")[1]));
    }

    [Theory]
    [InlineData("{last}", "file:///c:/windows/x.txt")]
    [InlineData("{last}", "javascript:alert(1)")]
    [InlineData("ftp://x.test/{last}", "a")]
    public void Only_http_and_https_are_opened(string template, string last)
        => Assert.Null(ContinueTargets.FillUrl(template, Values(Msg(ChatRole.User, last))));

    [Fact]
    public void An_https_template_is_opened() => Assert.NotNull(ContinueTargets.FillUrl("https://x.test/?q={chat}", Values(Msg(ChatRole.User, "hi"))));

    [Fact]
    public void A_long_chat_falls_back_to_the_last_message()
    {
        var messages = Enumerable.Range(0, 5000).Select(i => Msg(ChatRole.User, "0123456789")).Append(Msg(ChatRole.User, "the last one")).ToArray();
        var filled = ContinueTargets.FillUrl("https://x.test/?q={chat}", Values(messages))!;
        Assert.True(filled.Shortened);
        Assert.True(filled.Uri.AbsoluteUri.Length <= ContinueTargets.MaxUrlLength);
        Assert.EndsWith(Uri.EscapeDataString("the last one"), filled.Uri.AbsoluteUri);
    }

    [Fact]
    public void Commands_split_like_a_command_line()
    {
        var (program, args) = ContinueTargets.FillCommand("\"C:\\Program Files\\X\\x.exe\" --open \"{file}\"", Values(Msg(ChatRole.User, "hi")));
        Assert.Equal(@"C:\Program Files\X\x.exe", program);
        Assert.Equal(["--open", @"C:\Temp\chat.md"], args);
    }

    [Fact]
    public void Environment_variables_expand_in_the_command()
    {
        var (program, _) = ContinueTargets.FillCommand(@"%LOCALAPPDATA%\x.exe", Values(Msg(ChatRole.User, "hi")));
        Assert.Equal(Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "x.exe"), program);
    }

    [Fact]
    public void Chat_text_stays_one_argument()
    {
        var (_, args) = ContinueTargets.FillCommand("app.exe --prompt {last}", Values(Msg(ChatRole.User, "say \"hi\" & run calc")));
        Assert.Equal(["--prompt", "say \"hi\" & run calc"], args);
    }

    [Fact]
    public void The_lm_studio_entry_starts_one_program_whose_path_has_spaces()
    {
        var lm = ContinueTargets.ReadyMade.Single(e => e.Key == "lmstudio").Target;
        var (program, args) = ContinueTargets.FillCommand(lm.Command!, Values(Msg(ChatRole.User, "hi")));
        Assert.EndsWith(@"\Programs\LM Studio\LM Studio.exe", program);
        Assert.Empty(args);
    }

    [Fact]
    public void Placeholder_text_inside_the_chat_is_left_alone()
    {
        var (_, args) = ContinueTargets.FillCommand("app.exe {last}", Values(Msg(ChatRole.User, "use {file} and {model} in the template")));
        Assert.Equal(["use {file} and {model} in the template"], args);
    }

    [Fact]
    public void Ready_made_entries_match_the_spec()
    {
        var r = ContinueTargets.ReadyMade.ToDictionary(e => e.Key, e => e.Target);
        Assert.Equal("http://127.0.0.1:8080/?q={chat}&model={model}", r["llamacpp"].Url);
        Assert.Equal((ContinueKind.OpenWebUi, "http://localhost:3000"), (r["openwebui"].Kind, r["openwebui"].Url));
        Assert.Equal((ContinueKind.App, @"""%LOCALAPPDATA%\Programs\LM Studio\LM Studio.exe""", true),
            (r["lmstudio"].Kind, r["lmstudio"].Command, r["lmstudio"].CopyChat));
        Assert.Equal("https://chatgpt.com/?q={chat}", r["chatgpt"].Url);
        Assert.Equal("https://claude.ai/new?q={chat}", r["claude"].Url);
    }
}
