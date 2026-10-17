using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Codex;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>
/// OpenAI Codex CLI backend. Event shapes from codex-rs/exec/src/exec_events.rs (codex exec --json): thread.started,
/// turn.started, item.started/updated/completed, turn.completed, turn.failed, error.
/// </summary>
public sealed class CodexTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeLineProcessFactory _factory = new();
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static ChatMessage U(string id, string text, params Attachment[] a) => new(id, ChatRole.User, text, a, Now);
    private static ChatMessage A(string id, string text) => new(id, ChatRole.Assistant, text, [], Now);

    private const string ThreadStarted = """{"type":"thread.started","thread_id":"0199a213-81c0-7800-8aa1-bbab2a035a53"}""";
    private const string TurnStarted = """{"type":"turn.started"}""";
    private const string TurnCompleted = """{"type":"turn.completed","usage":{"input_tokens":10,"cached_input_tokens":0,"output_tokens":5,"reasoning_output_tokens":0}}""";
    private static string Item(string kind, string id, string type, string text) =>
        "{\"type\":\"item." + kind + "\",\"item\":{\"id\":\"" + id + "\",\"type\":\"" + type + "\",\"text\":" + JsonSerializer.Serialize(text) + "}}";
    private static string Message(string id, string text) => Item("completed", id, "agent_message", text);

    private static IEnumerable<string> Answer(params string[] messages) =>
        new[] { ThreadStarted, TurnStarted, Item("completed", "r0", "reasoning", "thinking…") }
            .Concat(messages.Select((m, i) => Message("item_" + i, m)))
            .Append(TurnCompleted);

    private CodexBackend New(string? exe = @"C:\codex.exe", BackendProfile? profile = null, Func<BackendProfile, string>? prompt = null) =>
        new(profile ?? new BackendProfile { Id = "cx", Name = "Codex", Type = BackendType.Codex }, () => exe, Path.Combine(_dir, "ws"),
            _factory, new FileLog(Path.Combine(_dir, "h.log")), prompt ?? (_ => "SYSTEM"), Path.Combine(_dir, "home"));

    private void Script(Func<string, IEnumerable<string>> respond) =>
        _factory.Create = () => new FakeLineProcess { Respond = respond, ExitWhenInputCompletes = true };

    private static async Task<string> Collect(IAsyncEnumerable<ChatDelta> s)
    {
        var text = "";
        await foreach (var d in s) text = d.ResetBefore ? d.Text : text + d.Text;
        return text;
    }

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    // ---- arguments ---------------------------------------------------------------------------

    private static async Task<CliSession?> SessionOf(IAsyncEnumerable<ChatDelta> stream)
    {
        CliSession? session = null;
        await foreach (var d in stream) session = d.Session ?? session;
        return session;
    }

    [Fact]
    public void Chat_only_is_read_only_ephemeral_and_ignores_the_users_config()
    {
        var args = CodexProtocol.BuildArgs(new BackendProfile { Type = BackendType.Codex, KeepCliSessions = false }, "Be brief.", null, []);
        Assert.Equal("exec", args[0]);
        Assert.Contains("--json", args);
        Assert.Contains("--skip-git-repo-check", args);
        Assert.Contains("--ephemeral", args); // only when Codex isn't keeping the chats (default: it is)
        Assert.DoesNotContain("--ephemeral", CodexProtocol.BuildArgs(new BackendProfile { Type = BackendType.Codex }, "x", null, []));
        Assert.Contains("--ignore-user-config", args);
        Assert.Contains("--ignore-rules", args);
        Assert.Equal("read-only", After(args, "--sandbox"));
        Assert.Contains("approval_policy=\"never\"", args);
        Assert.Contains("developer_instructions=\"Be brief.\"", args);
        Assert.Equal("-", args[^1]); // the prompt comes on stdin: no command-line length limit, no escaping
        Assert.DoesNotContain(args, a => a.Contains("dangerously", StringComparison.Ordinal));
    }

    [Fact]
    public void Inherit_mode_writes_in_the_workspace_and_bypasses_only_with_approve_all()
    {
        var inherit = CodexProtocol.BuildArgs(new BackendProfile { Tools = ToolMode.Inherit }, "x", null, []);
        Assert.Equal("workspace-write", After(inherit, "--sandbox"));
        Assert.DoesNotContain("--ignore-user-config", inherit);

        var all = CodexProtocol.BuildArgs(new BackendProfile { Tools = ToolMode.Inherit, ApproveAllTools = true }, "x", null, []);
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", all);
        Assert.DoesNotContain("--sandbox", all);

        var chatOnly = CodexProtocol.BuildArgs(new BackendProfile { ApproveAllTools = true }, "x", null, []);
        Assert.DoesNotContain("--dangerously-bypass-approvals-and-sandbox", chatOnly);
    }

    [Fact]
    public void Extra_args_cannot_bypass_the_sandbox()
    {
        var args = CodexProtocol.BuildArgs(new BackendProfile { ExtraArgs = "--search --dangerously-bypass-approvals-and-sandbox --sandbox danger-full-access --yolo -s danger-full-access -c \"approval_policy\"=\"on-request\" --config=sandbox_mode=workspace-write -c model=\"o3\"" }, "x", null, []);
        Assert.Contains("model=\"o3\"", args); // other config overrides pass
        Assert.DoesNotContain(args, a => a.Contains("on-request", StringComparison.Ordinal) || a.Contains("sandbox_mode", StringComparison.Ordinal));
        Assert.Contains("--search", args);
        Assert.DoesNotContain("--dangerously-bypass-approvals-and-sandbox", args);
        Assert.DoesNotContain("danger-full-access", args);
        Assert.DoesNotContain("--yolo", args);
        Assert.Equal("read-only", After(args, "--sandbox"));
    }

    [Theory]
    [InlineData("gpt-5.5-codex", "high", "gpt-5.5-codex", "high")]
    [InlineData(null, "xhigh", null, "xhigh")]
    [InlineData(null, "bogus", null, null)]
    public void Model_and_effort(string? model, string? effort, string? expectedModel, string? expectedEffort)
    {
        var args = CodexProtocol.BuildArgs(new BackendProfile { Model = model, Effort = effort }, "", null, []);
        if (expectedModel is null) Assert.DoesNotContain("--model", args); else Assert.Equal(expectedModel, After(args, "--model"));
        var effortArg = args.FirstOrDefault(a => a.StartsWith("model_reasoning_effort=", StringComparison.Ordinal));
        Assert.Equal(expectedEffort is null ? null : $"model_reasoning_effort=\"{expectedEffort}\"", effortArg);
        Assert.DoesNotContain(args, a => a.StartsWith("developer_instructions", StringComparison.Ordinal)); // empty prompt: nothing
    }

    [Fact]
    public void System_prompt_is_a_valid_toml_string_even_with_quotes_and_newlines()
    {
        var args = CodexProtocol.BuildArgs(new BackendProfile(), "Say \"hi\"\nthen \\stop", null, []);
        var arg = args.Single(a => a.StartsWith("developer_instructions=", StringComparison.Ordinal));
        Assert.Equal("developer_instructions=\"Say \\\"hi\\\"\\nthen \\\\stop\"", arg);
    }

    [Fact]
    public void Attached_short_flags_and_folder_flags_are_dropped_too()
    {
        var args = CodexProtocol.BuildArgs(new BackendProfile { ExtraArgs = "-sworkspace-write -csandbox_mode=\"workspace-write\" -C C:\\ --cd=D:\\ --add-dir C:\\Windows -cmodel=\"o3\"" }, "x", null, []);
        Assert.DoesNotContain(args, a => a.Contains("workspace-write", StringComparison.Ordinal) || a.Contains(":\\", StringComparison.Ordinal));
        Assert.DoesNotContain("-C", args);
        Assert.Contains("-cmodel=\"o3\"", args);
        Assert.Equal("read-only", After(args, "--sandbox"));
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("emoji 😀 é", "\"emoji 😀 é\"")]
    [InlineData("a\u007Fb\u0001", "\"a\\u007Fb\\u0001\"")]
    [InlineData("tab\there", "\"tab\\there\"")]
    public void Toml_strings(string value, string expected) => Assert.Equal(expected, CodexProtocol.Toml(value));

    [Theory]
    [InlineData("unexpected status 401 Unauthorized: Missing bearer", BackendErrorKind.NotLoggedIn)]
    [InlineData("stream error, request id: req_4012ab429c, retry after 5031ms", BackendErrorKind.Failed)]
    [InlineData("unexpected status 429 Too Many Requests", BackendErrorKind.RateLimited)]
    [InlineData("You've hit your usage limit.", BackendErrorKind.RateLimited)]
    [InlineData("unexpected status 503 Service Unavailable", BackendErrorKind.ServerDown)]
    public void Errors_map_on_whole_status_codes_only(string message, BackendErrorKind kind) => Assert.Equal(kind, CodexErrors.Map(message).Kind);

    [Fact]
    public void Resume_and_images()
    {
        var args = CodexProtocol.BuildArgs(new BackendProfile { KeepCliSessions = true }, "x", "thread-1", [@"C:\a.png", @"C:\b.jpg"]);
        Assert.DoesNotContain("--ephemeral", args); // a resumable session has to be saved
        var resume = args.ToList().IndexOf("resume");
        Assert.True(resume > 0);
        Assert.Equal("thread-1", args[resume + 1]);
        Assert.True(args.ToList().IndexOf("--json") < resume); // exec's options go before the subcommand
        Assert.Equal(["--image", @"C:\a.png", "--image", @"C:\b.jpg", "-"], args.Skip(resume + 2));
    }

    // ---- turn parsing --------------------------------------------------------------------------

    [Fact]
    public void Parser_shows_agent_messages_joined_and_ignores_reasoning_and_commands()
    {
        var p = new CodexTurnParser();
        var lines = new[]
        {
            ThreadStarted, TurnStarted,
            Item("completed", "r", "reasoning", "secret thoughts"),
            Message("m1", "First."),
            """{"type":"item.started","item":{"id":"c1","type":"command_execution","command":"ls","aggregated_output":"","exit_code":null,"status":"in_progress"}}""",
            Message("m2", "Second."),
            TurnCompleted,
        };
        var text = string.Concat(lines.SelectMany(p.Feed).Select(d => d.Text));
        Assert.Equal("First.\n\nSecond.", text);
        Assert.True(p.Completed);
        Assert.Null(p.Error);
        Assert.Equal("0199a213-81c0-7800-8aa1-bbab2a035a53", p.ThreadId);
    }

    [Fact]
    public void Parser_streams_updates_of_one_message_without_repeating_text()
    {
        var p = new CodexTurnParser();
        var deltas = new[]
        {
            Item("started", "m", "agent_message", "Hel"),
            Item("updated", "m", "agent_message", "Hello wor"),
            Item("completed", "m", "agent_message", "Hello world"),
        }.SelectMany(p.Feed).ToList();
        Assert.Equal(["Hel", "lo wor", "ld"], deltas.Select(d => d.Text));
        Assert.All(deltas, d => Assert.False(d.ResetBefore));
    }

    [Fact]
    public void Parser_resets_when_a_message_is_rewritten()
    {
        var p = new CodexTurnParser();
        var deltas = new[] { Item("updated", "m", "agent_message", "Draft"), Item("completed", "m", "agent_message", "Final") }.SelectMany(p.Feed).ToList();
        Assert.True(deltas[^1].ResetBefore);
        Assert.Equal("Final", deltas[^1].Text);
    }

    [Fact]
    public void Parser_reports_turn_failed_and_remembers_stream_errors()
    {
        var failed = new CodexTurnParser();
        var note = Assert.Single(failed.Feed("""{"type":"error","message":"Reconnecting... 1/5"}"""));
        Assert.Equal("Codex is reconnecting…", note.Status);
        Assert.False(failed.Completed); // stream errors can be retried; only turn.failed / exit ends the turn
        Assert.Equal("Reconnecting... 1/5", failed.LastError);
        _ = failed.Feed("""{"type":"turn.failed","error":{"message":"You've hit your usage limit."}}""").ToList();
        Assert.True(failed.Completed);
        Assert.Equal("You've hit your usage limit.", failed.Error);
    }

    [Fact]
    public void Parser_skips_junk_lines()
    {
        var p = new CodexTurnParser();
        Assert.Empty(p.Feed("not json"));
        Assert.Empty(p.Feed("[1,2]"));
        Assert.Empty(p.Feed("""{"type":"item.completed","item":{"id":"x","type":"todo_list","items":[]}}"""));
        Assert.False(p.Completed);
    }

    // ---- backend -------------------------------------------------------------------------------

    [Fact]
    public async Task One_process_per_turn_with_the_prompt_on_stdin_then_closed()
    {
        Script(_ => Answer("Hi there."));
        var text = await Collect(New().StreamAsync([U("u1", "hello")], default));
        Assert.Equal("Hi there.", text);
        var (exe, args, cwd, process) = Assert.Single(_factory.Started);
        Assert.Equal(@"C:\codex.exe", exe);
        Assert.Equal(Path.Combine(_dir, "home"), cwd); // chat only: the home folder (read-only sandbox), where `codex resume` finds it
        Assert.Equal("hello", Assert.Single(process.Written));
        Assert.True(process.InputCompleted);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task Later_turns_replay_the_conversation_in_a_fresh_ephemeral_session()
    {
        Script(_ => Answer("ok"));
        var b = New();
        await Collect(b.StreamAsync([U("u1", "first")], default));
        await Collect(b.StreamAsync([U("u1", "first"), A("a1", "ok"), U("u2", "second")], default));
        Assert.Equal(2, _factory.Started.Count);
        Assert.DoesNotContain("resume", _factory.Started[1].Args);
        var sent = Assert.Single(_factory.Started[1].Process.Written);
        Assert.Contains("<previous_conversation>", sent);
        Assert.Contains("first", sent);
        Assert.EndsWith("second", sent);
    }

    [Fact]
    public async Task Keep_sessions_resumes_the_thread_and_sends_only_the_new_message()
    {
        Script(_ => Answer("ok"));
        var b = New(profile: new BackendProfile { Id = "cx", Name = "Codex", Type = BackendType.Codex, KeepCliSessions = true });
        var session = await SessionOf(b.StreamAsync([U("u1", "first")], default));
        Assert.Equal("0199a213-81c0-7800-8aa1-bbab2a035a53", session?.Id); // reported for the chat to record
        b.Resume = session; // what the chat controller hands back on the next turn
        await Collect(b.StreamAsync([U("u1", "first"), A("a1", "ok"), U("u2", "second")], default));
        var args = _factory.Started[1].Args.ToList();
        Assert.Equal("0199a213-81c0-7800-8aa1-bbab2a035a53", args[args.IndexOf("resume") + 1]);
        Assert.Equal("second", Assert.Single(_factory.Started[1].Process.Written));

        // A new chat (no session handed back) starts over.
        b.Resume = null;
        await Collect(b.StreamAsync([U("x1", "other")], default));
        Assert.DoesNotContain("resume", _factory.Started[2].Args);
    }

    [Fact]
    public async Task A_resumed_turn_that_fails_after_starting_is_not_replayed()
    {
        _factory.Create = () => _factory.Started.Count == 1
            ? new FakeLineProcess { ExitWhenInputCompletes = true, Respond = _ => [TurnStarted, Message("m", "half an ans")] } // then dies
            : new FakeLineProcess { ExitWhenInputCompletes = true, Respond = _ => Answer("ok") };
        var b = New(profile: new BackendProfile { Id = "cx", Name = "Codex", Type = BackendType.Codex, KeepCliSessions = true });
        b.Resume = await SessionOf(b.StreamAsync([U("u1", "first")], default));
        await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([U("u1", "first"), A("a1", "ok"), U("u2", "second")], default)));
        Assert.Equal(2, _factory.Started.Count); // no silent replay that would repeat the half answer
    }

    [Fact]
    public void Model_check_message_for_a_connection_without_a_model_list()
    {
        Assert.Equal("Connected: 0 models.", ModelCatalog.Describe([], null));
        Assert.StartsWith("This connection doesn't list models", ModelCatalog.Describe([], null, listsModels: false));
        Assert.StartsWith("Using gpt-5.5", ModelCatalog.Describe([], "gpt-5.5", listsModels: false));
    }

    [Fact]
    public async Task Stopping_early_stops_codex()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => [ThreadStarted, Message("m", "partial")] }; // still running
        await foreach (var _ in New().StreamAsync([U("u1", "hi")], default)) break;
        Assert.True(_factory.Started[0].Process.Disposed);
    }

    [Fact]
    public async Task A_lost_saved_session_falls_back_to_replaying_the_chat()
    {
        _factory.Create = () => _factory.Started.Count == 1 // the 2nd process (the resume) dies at once, like codex with no rollout
            ? new FakeLineProcess { ExitWhenInputCompletes = true, StandardErrorTail = "Error: thread/resume failed: no rollout found" }
            : new FakeLineProcess { ExitWhenInputCompletes = true, Respond = _ => Answer("ok") };
        var b = New(profile: new BackendProfile { Id = "cx", Name = "Codex", Type = BackendType.Codex, KeepCliSessions = true });
        b.Resume = await SessionOf(b.StreamAsync([U("u1", "first")], default));
        Assert.Equal("ok", await Collect(b.StreamAsync([U("u1", "first"), A("a1", "ok"), U("u2", "second")], default)));
        Assert.Equal(3, _factory.Started.Count);
        Assert.Contains("resume", _factory.Started[1].Args);
        Assert.DoesNotContain("resume", _factory.Started[2].Args);
        Assert.Contains("<previous_conversation>", Assert.Single(_factory.Started[2].Process.Written));
    }

    [Fact]
    public async Task Signed_out_on_a_resumed_chat_says_so_instead_of_replaying()
    {
        _factory.Create = () => _factory.Started.Count == 1 // the resume dies at once: signed out
            ? new FakeLineProcess { ExitWhenInputCompletes = true, StandardErrorTail = "Error: Not logged in. Run codex login." }
            : new FakeLineProcess { ExitWhenInputCompletes = true, Respond = _ => Answer("ok") };
        var b = New(profile: new BackendProfile { Id = "cx", Name = "Codex", Type = BackendType.Codex, KeepCliSessions = true });
        b.Resume = await SessionOf(b.StreamAsync([U("u1", "first")], default));
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([U("u1", "first"), A("a1", "ok"), U("u2", "second")], default)));
        Assert.Equal(BackendErrorKind.NotLoggedIn, ex.Kind);
        Assert.Equal(2, _factory.Started.Count); // the thread is kept: no new session started
    }

    [Fact]
    public async Task Images_go_as_temporary_files_that_are_removed_afterwards()
    {
        string? path = null;
        _factory.Create = () => new FakeLineProcess
        {
            ExitWhenInputCompletes = true,
            Respond = _ =>
            {
                var args = _factory.Started[^1].Args.ToList();
                path = args[args.IndexOf("--image") + 1];
                Assert.True(File.Exists(path));
                Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
                return Answer("A cat.");
            },
        };
        var image = new Attachment("i1", "shot.png", AttachmentKind.Image, "image/png", [1, 2, 3]);
        Assert.Equal("A cat.", await Collect(New().StreamAsync([U("u1", "what is this", image)], default)));
        Assert.EndsWith(".png", path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Failures_map_to_friendly_errors()
    {
        Script(_ => [ThreadStarted, TurnStarted, """{"type":"turn.failed","error":{"message":"You've hit your usage limit. Try again later."}}"""]);
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New().StreamAsync([U("u1", "hi")], default)));
        Assert.Equal(BackendErrorKind.RateLimited, ex.Kind);

        _factory.Create = () => new FakeLineProcess { ExitWhenInputCompletes = true, StandardErrorTail = "Error: Not logged in. Run codex login." };
        ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New().StreamAsync([U("u1", "hi")], default)));
        Assert.Equal(BackendErrorKind.NotLoggedIn, ex.Kind);

        ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New(exe: null).StreamAsync([U("u1", "hi")], default)));
        Assert.Equal(BackendErrorKind.NotInstalled, ex.Kind);
    }

    [Fact]
    public async Task Inherit_mode_runs_in_the_working_folder()
    {
        var work = Directory.CreateDirectory(Path.Combine(_dir, "proj")).FullName;
        Script(_ => Answer("done"));
        await Collect(New(profile: new BackendProfile { Id = "cx", Type = BackendType.Codex, Tools = ToolMode.Inherit, WorkingDirectory = work }).StreamAsync([U("u1", "go")], default));
        Assert.Equal(work, _factory.Started[0].Cwd);
    }

    // ---- locating codex ------------------------------------------------------------------------

    [Fact]
    public void Locator_prefers_the_configured_path_then_native_exe_on_path_then_the_npm_package_binary()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Exists(string p) => files.Contains(p);
        Assert.Null(CodexLocator.Find(null, Exists, @"C:\npm;C:\bin", "x64"));

        var npmBinary = @"C:\npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe";
        files.Add(npmBinary);
        Assert.Equal(npmBinary, CodexLocator.Find(null, Exists, @"C:\npm;C:\bin", "x64"));

        var arm = @"C:\npm\node_modules\@openai\codex\vendor\aarch64-pc-windows-msvc\bin\codex.exe";
        files.Add(arm);
        Assert.Equal(arm, CodexLocator.Find(null, Exists, @"C:\npm", "arm64"));

        files.Add(@"C:\bin\codex.exe");
        Assert.Equal(@"C:\bin\codex.exe", CodexLocator.Find(null, Exists, @"C:\npm;C:\bin", "x64"));

        files.Add(@"D:\tools\codex.exe");
        Assert.Equal(@"D:\tools\codex.exe", CodexLocator.Find(@"D:\tools\codex.exe", Exists, @"C:\bin", "x64"));
        Assert.Null(CodexLocator.Find(@"D:\missing.exe", Exists, @"C:\bin", "x64"));
    }

    // ---- wiring --------------------------------------------------------------------------------

    [Fact]
    public void Codex_is_a_cli_connection_type()
    {
        var info = ConnectionTypes.Of(BackendType.Codex);
        Assert.True(info.Has(ConnectionField.CliPath) && info.Has(ConnectionField.Tools));
        Assert.False(info.Has(ConnectionField.ApiKey));
        Assert.Equal(["low", "medium", "high", "xhigh", "max", "ultra"], info.EffortLevels);
    }
}
