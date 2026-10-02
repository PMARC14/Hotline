using System.Text.Json;
using Hotline.Core.Backends.ClaudeCode;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Claude Code CLI backend. Event shapes recorded from claude 2.1.287 (-p, stream-json, partial messages).</summary>
public sealed class ClaudeCodeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeLineProcessFactory _factory = new();
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static ChatMessage U(string id, string text, params Attachment[] a) => new(id, ChatRole.User, text, a, Now);
    private static ChatMessage A(string id, string text) => new(id, ChatRole.Assistant, text, [], Now);

    private static string Delta(string text) =>
        "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":" + JsonSerializer.Serialize(text) + "}}}";
    private const string MessageStart = """{"type":"stream_event","event":{"type":"message_start","message":{"role":"assistant"}}}""";
    private const string Thinking = """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"hmm"}}}""";
    private static string Result(string text, bool error = false, string subtype = "success") =>
        "{\"type\":\"result\",\"subtype\":\"" + subtype + "\",\"is_error\":" + (error ? "true" : "false") + ",\"result\":" + JsonSerializer.Serialize(text) + ",\"session_id\":\"s1\"}";

    private static IEnumerable<string> Answer(params string[] pieces) =>
        new[] { """{"type":"system","subtype":"init","session_id":"s1","tools":[]}""", MessageStart, Thinking }
            .Concat(pieces.Select(Delta))
            .Append($$$"""{"type":"assistant","message":{"content":[{"type":"text","text":{{{JsonSerializer.Serialize(string.Concat(pieces))}}}}]}}""")
            .Append(Result(string.Concat(pieces)));

    private ClaudeCodeBackend New(string? exe = @"C:\claude.exe", BackendProfile? profile = null, Func<BackendProfile, string>? prompt = null) =>
        new(profile ?? new BackendProfile { Id = "cc", Name = "Claude", Type = BackendType.ClaudeCode }, () => exe, Path.Combine(_dir, "ws"),
            _factory, new FileLog(Path.Combine(_dir, "h.log")), prompt ?? (_ => "SYSTEM"), Path.Combine(_dir, "home"));

    private static async Task<string> Collect(IAsyncEnumerable<ChatDelta> s)
    {
        var text = "";
        await foreach (var d in s) text = d.ResetBefore ? d.Text : text + d.Text;
        return text;
    }

    private static JsonElement Message(string line) => JsonDocument.Parse(line).RootElement.GetProperty("message");

    // ---- arguments ---------------------------------------------------------------------------

    [Fact]
    public void Chat_only_is_isolated_from_the_users_tools_hooks_and_mcp()
    {
        var args = ClaudeCodeProtocol.BuildArgs(new BackendProfile { Type = BackendType.ClaudeCode }, "Be brief.").ToList();
        Assert.Contains("-p", args);
        Assert.Equal("stream-json", args[args.IndexOf("--input-format") + 1]);
        Assert.Equal("stream-json", args[args.IndexOf("--output-format") + 1]);
        Assert.Contains("--include-partial-messages", args);
        Assert.Contains("--verbose", args);
        Assert.Equal("", args[args.IndexOf("--tools") + 1]);
        Assert.Equal("", args[args.IndexOf("--setting-sources") + 1]);
        Assert.Contains("--strict-mcp-config", args);
        Assert.Equal("none", args[args.IndexOf("--permission-prompts") + 1]);
        Assert.Equal("Be brief.", args[args.IndexOf("--append-system-prompt") + 1]);
        Assert.DoesNotContain("--bare", args); // --bare never reads the user's Claude login
    }

    [Fact]
    public void Inherit_mode_uses_the_users_setup_and_approve_all_only_there()
    {
        var inherit = ClaudeCodeProtocol.BuildArgs(new BackendProfile { Tools = ToolMode.Inherit, ApproveAllTools = true }, "x").ToList();
        Assert.DoesNotContain("--setting-sources", inherit);
        Assert.DoesNotContain("--tools", inherit);
        Assert.Contains("--dangerously-skip-permissions", inherit);
        Assert.DoesNotContain("--dangerously-skip-permissions", ClaudeCodeProtocol.BuildArgs(new BackendProfile { ApproveAllTools = true }, "x"));
    }

    [Theory]
    [InlineData("opus", "max", "opus", "max")]
    [InlineData("sonnet", "xhigh", "sonnet", "xhigh")]
    [InlineData(null, "bogus", null, null)]
    public void Model_and_effort(string? model, string? effort, string? expectedModel, string? expectedEffort)
    {
        var args = ClaudeCodeProtocol.BuildArgs(new BackendProfile { Model = model, Effort = effort }, "x").ToList();
        Assert.Equal(expectedModel, args.Contains("--model") ? args[args.IndexOf("--model") + 1] : null);
        Assert.Equal(expectedEffort, args.Contains("--effort") ? args[args.IndexOf("--effort") + 1] : null);
    }

    [Fact]
    public void Extra_args_cannot_smuggle_in_dangerous_flags()
        => Assert.DoesNotContain(ClaudeCodeProtocol.BuildArgs(new BackendProfile { ExtraArgs = "--dangerously-skip-permissions --allow-dangerously-skip-permissions --permission-mode bypassPermissions" }, "x"),
            a => a.Contains("dangerous", StringComparison.OrdinalIgnoreCase) || a == "bypassPermissions");

    [Fact]
    public void Locator_prefers_configured_then_native_install_then_path()
    {
        var files = new HashSet<string> { @"C:\Users\u\.local\bin\claude.exe", @"D:\tools\claude.exe" };
        Assert.Equal(@"D:\tools\claude.exe", ClaudeLocator.Find(@"D:\tools\claude.exe", files.Contains, @"C:\Users\u", null));
        Assert.Equal(@"C:\Users\u\.local\bin\claude.exe", ClaudeLocator.Find(null, files.Contains, @"C:\Users\u", @"D:\tools"));
        Assert.Equal(@"D:\tools\claude.exe", ClaudeLocator.Find(null, f => f == @"D:\tools\claude.exe", @"C:\Users\u", @"D:\tools"));
        Assert.Null(ClaudeLocator.Find(null, _ => false, @"C:\Users\u", @"D:\tools"));
    }

    // ---- stream parsing ----------------------------------------------------------------------

    [Fact]
    public void Text_deltas_stream_and_thinking_is_not_shown()
    {
        var parser = new ClaudeTurnParser();
        var text = string.Concat(Answer("Hel", "lo").SelectMany(parser.Feed).Select(d => d.Text));
        Assert.Equal("Hello", text);
        Assert.True(parser.Completed);
        Assert.Null(parser.Error);
    }

    [Fact]
    public void Multiple_assistant_messages_in_one_turn_are_separated()
    {
        var parser = new ClaudeTurnParser();
        var lines = new[] { MessageStart, Delta("Checking the folder."), MessageStart, Delta("It has 3 files."), Result("It has 3 files.") };
        Assert.Equal("Checking the folder.\n\nIt has 3 files.", string.Concat(lines.SelectMany(parser.Feed).Select(d => d.Text)));
    }

    [Fact]
    public void Without_partial_messages_the_assistant_text_is_used()
    {
        var parser = new ClaudeTurnParser();
        var lines = new[] { """{"type":"assistant","message":{"content":[{"type":"thinking","thinking":""},{"type":"text","text":"Red."}]}}""", Result("Red.") };
        Assert.Equal("Red.", string.Concat(lines.SelectMany(parser.Feed).Select(d => d.Text)));
    }

    [Fact]
    public void Error_results_are_reported()
    {
        var parser = new ClaudeTurnParser();
        _ = parser.Feed(Result("Invalid API key · Please run /login", error: true)).ToList();
        Assert.True(parser.Completed);
        Assert.Contains("/login", parser.Error);
        Assert.Equal(BackendErrorKind.NotLoggedIn, ClaudeErrors.Map(parser.Error!).Kind);
        Assert.Equal(BackendErrorKind.RateLimited, ClaudeErrors.Map("Claude AI usage limit reached|1759450000").Kind);
    }

    // ---- backend -----------------------------------------------------------------------------

    [Fact]
    public async Task Missing_cli_is_NotInstalled()
    {
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New(exe: null).StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.NotInstalled, ex.Kind);
    }

    [Fact]
    public async Task Streams_answer_and_keeps_one_process_across_turns()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("Hi", "!") };
        var b = New();
        Assert.Equal("Hi!", await Collect(b.StreamAsync([U("1", "hello")], default)));
        Assert.Equal("Hi!", await Collect(b.StreamAsync([U("1", "hello"), A("2", "Hi!"), U("3", "again")], default)));
        var (_, _, cwd, proc) = Assert.Single(_factory.Started);
        Assert.EndsWith("ws", cwd);
        Assert.Equal("again", Message(proc.Written[1]).GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Images_are_sent_inline_as_base64_content_blocks()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("Red.") };
        var img = new Attachment("a1", "shot.png", AttachmentKind.Image, "image/png", [1, 2, 3]);
        await Collect(New().StreamAsync([U("1", "colour?", img)], default));
        var content = Message(_factory.Started[0].Process.Written[0]).GetProperty("content");
        var image = content.EnumerateArray().Single(c => c.GetProperty("type").GetString() == "image");
        Assert.Equal("image/png", image.GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), image.GetProperty("source").GetProperty("data").GetString());
    }

    [Fact]
    public async Task Restarted_session_replays_the_conversation_as_context()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var b = New();
        await Collect(b.StreamAsync([U("1", "first question"), A("2", "first answer"), U("3", "follow up")], default));
        var text = Message(_factory.Started[0].Process.Written[0]).GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("first question", text);
        Assert.Contains("first answer", text);
        Assert.EndsWith("follow up", text);
    }

    [Fact]
    public async Task Changed_system_prompt_restarts_with_the_new_prompt()
    {
        var prompt = "one";
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var b = New(prompt: _ => prompt);
        await Collect(b.StreamAsync([U("1", "hi")], default));
        prompt = "two";
        await Collect(b.StreamAsync([U("1", "hi"), A("2", "ok"), U("3", "again")], default));
        Assert.Equal(2, _factory.Started.Count);
        var args = _factory.Started[1].Args.ToList();
        Assert.Equal("two", args[args.IndexOf("--append-system-prompt") + 1]);
    }

    [Fact]
    public async Task Inherit_mode_runs_in_the_working_folder()
    {
        var work = Directory.CreateDirectory(Path.Combine(_dir, "project")).FullName;
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        await Collect(New(profile: new BackendProfile { Id = "cc", Name = "C", Type = BackendType.ClaudeCode, Tools = ToolMode.Inherit, WorkingDirectory = work })
            .StreamAsync([U("1", "list files")], default));
        Assert.Equal(work, _factory.Started[0].Cwd);
    }

    [Fact]
    public async Task Error_result_throws_mapped_error()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => [Result("Claude AI usage limit reached", error: true)] };
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New().StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.RateLimited, ex.Kind);
    }

    [Fact]
    public async Task Process_exit_mid_turn_reports_stderr()
    {
        _factory.Create = () =>
        {
            var p = new FakeLineProcess { StandardErrorTail = "Error: not logged in. Run /login" };
            p.Respond = _ => { p.Exit(); return []; };
            return p;
        };
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New().StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.NotLoggedIn, ex.Kind);
    }

    [Fact]
    public void Sessions_are_not_saved_by_claude_unless_asked()
    {
        Assert.Contains("--no-session-persistence", ClaudeCodeProtocol.BuildArgs(new BackendProfile(), "x"));
        Assert.DoesNotContain("--no-session-persistence", ClaudeCodeProtocol.BuildArgs(new BackendProfile { KeepCliSessions = true }, "x"));
    }

    [Fact]
    public void Custom_args_replace_the_mode_flags_but_not_the_protocol_or_safety()
    {
        var args = ClaudeCodeProtocol.BuildArgs(new BackendProfile { Args = ["--tools", "Read", "--mcp-config", @"C:\mcp.json", "--dangerously-skip-permissions"] }, "x").ToList();
        Assert.Equal("Read", args[args.IndexOf("--tools") + 1]);
        Assert.Contains(@"C:\mcp.json", args);
        Assert.DoesNotContain("--setting-sources", args);               // mode defaults replaced
        Assert.Contains("--include-partial-messages", args);            // protocol kept
        Assert.Equal("none", args[args.IndexOf("--permission-prompts") + 1]);
        Assert.DoesNotContain("--dangerously-skip-permissions", args);  // only via ApproveAllTools
    }
}
