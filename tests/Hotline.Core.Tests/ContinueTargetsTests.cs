using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Continue in targets: which list a chat gets, the defaults and the ready-made entries.</summary>
public sealed class ContinueTargetsTests : TempDirTest
{

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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void A_local_connection_without_an_endpoint_has_no_default_target(string? endpoint)
    {
        var local = Local();
        local.Endpoint = endpoint;
        Assert.Empty(ContinueTargets.Shared(With(Api(), local)));
    }

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

    public static TheoryData<string, string, string?, string?, string[]?> Commands =>
    [
        ("\"C:\\Program Files\\X\\x.exe\" --open \"{file}\"", "hi", @"C:\Program Files\X\x.exe", null, ["--open", @"C:\Temp\chat.md"]),
        (@"%LOCALAPPDATA%\x.exe", "hi", Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "x.exe"), null, null),
        ("app.exe --prompt {last}", "say \"hi\" & run calc", null, null, ["--prompt", "say \"hi\" & run calc"]),
        (ContinueTargets.ReadyMade.Single(e => e.Key == "lmstudio").Target.Command!, "hi", null, @"\Programs\LM Studio\LM Studio.exe", []),
        ("app.exe {last}", "use {file} and {model} in the template", null, null, ["use {file} and {model} in the template"]),
    ];

    [Theory]
    [MemberData(nameof(Commands))]
    public void Commands_preserve_program_paths_arguments_and_placeholders(
        string command, string last, string? expectedProgram, string? programSuffix, string[]? expectedArgs)
    {
        var (program, args) = ContinueTargets.FillCommand(command, Values(Msg(ChatRole.User, last)));
        if (expectedProgram is not null) Assert.Equal(expectedProgram, program);
        if (programSuffix is not null) Assert.EndsWith(programSuffix, program);
        if (expectedArgs is not null) Assert.Equal(expectedArgs, args);
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
        // Gemini's site takes no message in its link: it opens Gemini with the chat on the clipboard to paste.
        Assert.Equal((ContinueKind.Web, "Gemini", "https://gemini.google.com/app", true),
            (r["gemini"].Kind, r["gemini"].Name, r["gemini"].Url, r["gemini"].CopyChat));
    }

    [Theory]
    [InlineData("tool.bat", true)]
    [InlineData(@"C:\x\Run.CMD", true)]
    [InlineData(@"""C:\a b\x.cmd""", true)]
    [InlineData("tool.exe", false)]
    [InlineData("tool", false)]
    [InlineData("", false)]
    public void Batch_files_are_never_started(string program, bool batch) => Assert.Equal(batch, ContinueTargets.IsBatchFile(program));

    [Fact]
    public void Targets_are_written_without_empty_fields()
    {
        var json = SettingsStore.Serialize(new HotlineSettings { Chat = { ContinueIn = [new ContinueTarget(ContinueKind.OpenWebUi, "W", Url: "http://x")] } });
        Assert.DoesNotContain("command", json);
        Assert.DoesNotContain("model", json);
        Assert.Contains("\"kind\": \"openWebUi\"", json);
    }
}
