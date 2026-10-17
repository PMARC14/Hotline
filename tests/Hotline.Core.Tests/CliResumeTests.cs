using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Backends.ClaudeCode;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Claude Code and Antigravity resume a chat's own CLI conversation (Plan 9).</summary>
public sealed class CliResumeTests : IDisposable
{
    private const string SessionA = "6a8b2362-20cc-4208-bd05-cf9b25fe01c9";
    private const string SessionB = "e0673a6a-7fb2-44fb-8274-3dc847e1a2ce";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeLineProcessFactory _factory = new();
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static ChatMessage U(string id, string text) => new(id, ChatRole.User, text, [], Now);
    private static ChatMessage A(string id, string text) => new(id, ChatRole.Assistant, text, [], Now);

    private static async Task<(string Text, List<CliSession> Sessions)> Collect(IAsyncEnumerable<ChatDelta> s)
    {
        var text = "";
        var sessions = new List<CliSession>();
        await foreach (var d in s)
        {
            if (d.Session is { } session) sessions.Add(session);
            text = d.ResetBefore ? d.Text : text + d.Text;
        }
        return (text, sessions);
    }

    // ---- Claude Code

    private static IEnumerable<string> ClaudeAnswer(string session, string text) =>
    [
        "{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"" + session + "\",\"tools\":[]}",
        "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(text) + "}]}}",
        "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":" + JsonSerializer.Serialize(text) + ",\"session_id\":\"" + session + "\"}",
    ];

    private ClaudeCodeBackend Claude(bool keep = true) =>
        new(new BackendProfile { Id = "cc", Name = "Claude", Type = BackendType.ClaudeCode, KeepCliSessions = keep }, () => @"C:\claude.exe",
            _factory, new FileLog(Path.Combine(_dir, "h.log")), _ => "SYSTEM", Path.Combine(_dir, "home"));

    [Fact]
    public async Task Claude_reports_its_session_with_the_folder()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => ClaudeAnswer(SessionA, "hi!") };
        var (text, sessions) = await Collect(Claude().StreamAsync([U("1", "hi")], default));
        Assert.Equal("hi!", text);
        Assert.Equal(new CliSession(SessionA, Path.Combine(_dir, "home")), Assert.Single(sessions)); // chat only runs in the home folder
        Assert.DoesNotContain("--resume", _factory.Started[0].Args);
    }

    [Fact]
    public async Task Claude_resumes_in_the_original_folder_without_replaying()
    {
        var folder = Path.Combine(_dir, "where-it-began");
        Directory.CreateDirectory(folder);
        _factory.Create = () => new FakeLineProcess { Respond = _ => ClaudeAnswer(SessionA, "walrus") };
        var backend = Claude();
        backend.Resume = new CliSession(SessionA, folder);
        var (text, _) = await Collect(backend.StreamAsync([U("1", "remember walrus"), A("2", "ok"), U("3", "what word?")], default));

        Assert.Equal("walrus", text);
        var (_, args, cwd, process) = Assert.Single(_factory.Started);
        Assert.Equal(["--resume", SessionA], args.Skip(args.ToList().IndexOf("--resume")).Take(2));
        Assert.Equal(folder, cwd);
        Assert.DoesNotContain("previous_conversation", Assert.Single(process.Written)); // the CLI has the context
    }

    [Fact]
    public async Task Claude_resumes_in_its_folder_even_if_it_was_removed()
    {
        var folder = Path.Combine(_dir, "gone", "Hotline");
        _factory.Create = () => new FakeLineProcess { Respond = _ => ClaudeAnswer(SessionA, "ok") };
        var backend = Claude();
        backend.Resume = new CliSession(SessionA, folder);
        await Collect(backend.StreamAsync([U("1", "hi")], default));
        Assert.Equal(folder, _factory.Started[0].Cwd);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task Claude_with_an_unknown_session_starts_over_and_replays()
    {
        var calls = 0;
        _factory.Create = () => ++calls == 1
            ? new FakeLineProcess { Respond = _ => ["{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"result\":\"\",\"session_id\":\"" + SessionA + "\"}"] }
            : new FakeLineProcess { Respond = _ => ClaudeAnswer(SessionB, "fresh") };
        var backend = Claude();
        backend.Resume = new CliSession(SessionA, "");
        var (text, sessions) = await Collect(backend.StreamAsync([U("1", "remember walrus"), A("2", "ok"), U("3", "what word?")], default));

        Assert.Equal("fresh", text);
        Assert.Equal(2, _factory.Started.Count);
        Assert.DoesNotContain("--resume", _factory.Started[1].Args);
        Assert.Contains("previous_conversation", _factory.Started[1].Process.Written.Single());
        Assert.Equal(SessionB, Assert.Single(sessions).Id);
    }

    [Fact]
    public async Task Claude_record_only_chat_that_cant_resume_says_so()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => ["{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"result\":\"\"}"] };
        var backend = Claude();
        backend.Resume = new CliSession(SessionA, "");
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(backend.StreamAsync([U("1", "what word?")], default)));
        Assert.Contains("Claude", ex.Message);
    }

    [Fact]
    public async Task Claude_doesnt_resume_when_sessions_arent_kept()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => ClaudeAnswer(SessionA, "ok") };
        var backend = Claude(keep: false);
        backend.Resume = new CliSession(SessionA, "");
        await Collect(backend.StreamAsync([U("1", "hi")], default));
        Assert.DoesNotContain("--resume", _factory.Started[0].Args);
        Assert.Contains("--no-session-persistence", _factory.Started[0].Args);
    }

    [Fact]
    public async Task Claude_new_chat_while_running_starts_a_new_session()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => ClaudeAnswer(SessionA, "ok") };
        var backend = Claude();
        await Collect(backend.StreamAsync([U("1", "one")], default));
        backend.Resume = new CliSession(SessionA, "");
        await Collect(backend.StreamAsync([U("1", "one"), A("2", "ok"), U("3", "two")], default));
        Assert.Single(_factory.Started); // same chat, same running session
        backend.Resume = null; // New chat
        await Collect(backend.StreamAsync([U("9", "fresh")], default));
        Assert.Equal(2, _factory.Started.Count);
        Assert.DoesNotContain("--resume", _factory.Started[1].Args);
    }

    // ---- Antigravity

    private static IEnumerable<string> AgyAnswer(string session, string text) =>
    [
        "{\"event\":\"init\",\"conversation_id\":\"" + session + "\",\"init\":{\"tools\":[]}}",
        "{\"event\":\"step_update\",\"step_update\":{\"step_index\":1,\"state\":\"DONE\",\"step_type\":\"agent_response\",\"text_delta\":" + JsonSerializer.Serialize(text) + "}}",
        "{\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"response\":" + JsonSerializer.Serialize(text) + "}}",
    ];

    private string ProjectsDir => Path.Combine(_dir, "home", ".gemini", "config", "projects");

    private AgyBackend Agy(bool keep = true, ToolMode tools = ToolMode.ChatOnly) =>
        new(new BackendProfile { Id = "agy", Name = "Gemini (Antigravity)", Agent = "hotline", KeepCliSessions = keep, Tools = tools }, () => @"C:\agy.exe",
            new AgyWorkspace(Path.Combine(_dir, "agy", "Hotline"), new ManualTimeProvider()), _factory, new FileLog(Path.Combine(_dir, "h.log")),
            _ => "SYSTEM", Path.Combine(_dir, "home"), new AgyProjects(ProjectsDir, Path.Combine(_dir, "old", "agy", "Hotline")));

    [Fact]
    public async Task Agy_chat_only_runs_in_the_home_folder_with_its_workspace_added()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => AgyAnswer(SessionA, "ok") };
        var (_, sessions) = await Collect(Agy().StreamAsync([U("1", "hi")], default));
        var started = _factory.Started[0];
        Assert.Equal(Path.Combine(_dir, "home"), started.Cwd); // `agy --conversation` from a home terminal finds it
        Assert.Equal(["--add-dir", Path.Combine(_dir, "agy", "Hotline")], started.Args.Skip(started.Args.ToList().IndexOf("--add-dir")).Take(2));
        Assert.Contains("--agent", started.Args); // the hotline agent lives in the added workspace
        Assert.DoesNotContain("--new-project", started.Args); // it would be named after the home folder
        Assert.Equal(Path.Combine(_dir, "home"), Assert.Single(sessions).WorkingDirectory);
    }

    [Fact]
    public async Task Agy_chat_only_joins_the_hotline_project_when_there_is_one()
    {
        Directory.CreateDirectory(ProjectsDir);
        var folder = new Uri(Path.Combine(_dir, "old", "agy", "Hotline")).AbsoluteUri.Replace("file:///", "file://");
        File.WriteAllText(Path.Combine(ProjectsDir, "p1.json"),
            "{\"id\":\"0f6c2d1e-aaaa-4bbb-8ccc-123456789abc\",\"name\":\"Hotline\",\"projectResources\":{\"resources\":[{\"folderUri\":\"" + folder + "\"}]}}");
        _factory.Create = () => new FakeLineProcess { Respond = _ => AgyAnswer(SessionA, "ok") };
        await Collect(Agy().StreamAsync([U("1", "hi")], default));
        var args = _factory.Started[0].Args;
        Assert.Equal(["--project", "0f6c2d1e-aaaa-4bbb-8ccc-123456789abc"], args.Skip(args.ToList().IndexOf("--project")).Take(2));
    }

    [Fact]
    public async Task Agy_images_are_passed_by_full_path()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => AgyAnswer(SessionA, "ok") };
        var user = U("1", "look") with { Attachments = [new Attachment("a1", "shot.png", AttachmentKind.Image, "image/png", [1, 2, 3])] };
        await Collect(Agy().StreamAsync([user], default));
        var line = _factory.Started[0].Process.Written.Single();
        var prompt = System.Text.Json.JsonDocument.Parse(line).RootElement.GetProperty("message").GetProperty("content").GetString()!;
        Assert.Contains(Path.Combine(_dir, "agy", "Hotline", "attachments"), prompt);
    }

    [Fact]
    public async Task Agy_resume_gets_no_project_flags()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => AgyAnswer(SessionA, "pelican") };
        var backend = Agy();
        backend.Resume = new CliSession(SessionA, "");
        var (text, _) = await Collect(backend.StreamAsync([U("1", "what word?")], default));
        Assert.Equal("pelican", text);
        var args = _factory.Started[0].Args;
        Assert.Equal(["--conversation", SessionA], args.Skip(args.ToList().IndexOf("--conversation")).Take(2));
        Assert.DoesNotContain("--project", args);
        Assert.DoesNotContain("--new-project", args);
    }

    [Fact]
    public async Task Agy_that_starts_a_different_conversation_is_a_failed_resume()
    {
        var calls = 0;
        _factory.Create = () => new FakeLineProcess { Respond = _ => AgyAnswer(++calls == 1 ? SessionB : SessionB, "answer") };
        var backend = Agy();
        backend.Resume = new CliSession(SessionA, "");
        var (text, sessions) = await Collect(backend.StreamAsync([U("1", "remember pelican"), A("2", "ok"), U("3", "what word?")], default));

        Assert.Equal("answer", text);
        Assert.Equal(2, _factory.Started.Count); // gave up on the unknown ID, replayed in a new conversation
        Assert.DoesNotContain("--conversation", _factory.Started[1].Args);
        Assert.Contains("remember pelican", _factory.Started[1].Process.Written.Single());
        Assert.Equal(SessionB, Assert.Single(sessions).Id);
    }

    [Fact]
    public async Task Agy_inherit_mode_gets_no_project_flags()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "home"));
        _factory.Create = () => new FakeLineProcess { Respond = _ => AgyAnswer(SessionA, "ok") };
        await Collect(Agy(tools: ToolMode.Inherit).StreamAsync([U("1", "hi")], default));
        Assert.DoesNotContain("--new-project", _factory.Started[0].Args);
    }

    [Fact]
    public void Agy_projects_never_creates_one_and_finds_the_old_folder_but_not_a_name()
    {
        var old = Path.Combine(_dir, "old", "Hotline");
        var projects = new AgyProjects(ProjectsDir, old);
        Assert.Empty(projects.ArgsFor(Path.Combine(_dir, "new", "Hotline")));
        Directory.CreateDirectory(ProjectsDir);
        File.WriteAllText(Path.Combine(ProjectsDir, "mine.json"), "{\"id\":\"0f6c2d1e-aaaa-4bbb-8ccc-123456789abd\",\"name\":\"Hotline\",\"projectResources\":{}}");
        Assert.Empty(projects.ArgsFor(Path.Combine(_dir, "new", "Hotline"))); // the user's own project called Hotline
        var uri = new Uri(old).AbsoluteUri.Replace("file:///", "file://");
        File.WriteAllText(Path.Combine(ProjectsDir, "p.json"), "{\"id\":\"0f6c2d1e-aaaa-4bbb-8ccc-123456789abe\",\"name\":\"Hotline\",\"projectResources\":{\"resources\":[{\"folderUri\":\"" + uri + "\"}]}}");
        Assert.Equal(["--project", "0f6c2d1e-aaaa-4bbb-8ccc-123456789abe"], projects.ArgsFor(Path.Combine(_dir, "new", "Hotline")));
    }

    // ---- Continue in…

    [Fact]
    public void Continue_in_offers_the_chats_own_cli_and_the_others_under_more()
    {
        BackendType[] all = [BackendType.ClaudeCode, BackendType.Antigravity, BackendType.Codex];
        Assert.Equal(
            [new(BackendType.Codex, CliHandoff.Way.Resume, false), new(BackendType.ClaudeCode, CliHandoff.Way.Handoff, true),
             new(BackendType.Antigravity, CliHandoff.Way.Handoff, true)],
            CliHandoff.Choices(BackendType.Codex, hasSession: true, wholeChat: true, all));
    }

    [Fact]
    public void Continue_in_without_a_session_yet_still_offers_only_the_own_cli_on_top()
    {
        var choices = CliHandoff.Choices(BackendType.Codex, hasSession: false, wholeChat: true, [BackendType.Codex, BackendType.Antigravity]);
        Assert.Equal([new(BackendType.Codex, CliHandoff.Way.Handoff, false), new(BackendType.Antigravity, CliHandoff.Way.Handoff, true)], choices);
    }

    [Fact]
    public void Continue_in_a_record_only_chat_can_only_resume()
        => Assert.Equal([new(BackendType.ClaudeCode, CliHandoff.Way.Resume, false)],
            CliHandoff.Choices(BackendType.ClaudeCode, hasSession: true, wholeChat: false, [BackendType.ClaudeCode, BackendType.Codex]));

    [Fact]
    public void Continue_in_an_api_chat_lists_the_clis_directly()
        => Assert.Equal([new(BackendType.ClaudeCode, CliHandoff.Way.Handoff, false), new(BackendType.Codex, CliHandoff.Way.Handoff, false)],
            CliHandoff.Choices(BackendType.Gemini, hasSession: false, wholeChat: true, [BackendType.Codex, BackendType.ClaudeCode]));

    [Fact]
    public void Continue_in_resumes_the_chats_own_conversation()
    {
        var s = new CliSession(SessionA, @"C:\w\Hotline");
        Assert.Equal(["--conversation", SessionA], CliHandoff.Resume(BackendType.Antigravity, "agy.exe", s).Args);
        Assert.Equal(["--resume", SessionA], CliHandoff.Resume(BackendType.ClaudeCode, "claude.exe", s).Args);
        Assert.Equal(@"C:\w\Hotline", CliHandoff.Resume(BackendType.ClaudeCode, "claude.exe", s).WorkingDirectory);
        Assert.Throws<ArgumentException>(() => CliHandoff.Resume(BackendType.ClaudeCode, "claude.exe", s with { Id = "--evil-flag" }));
    }

    [Fact]
    public void Continue_in_another_cli_points_it_at_a_file_never_the_chat_text()
    {
        var c = CliHandoff.Handoff(BackendType.Antigravity, "agy.exe", @"C:\h\chat.md", @"C:\h");
        Assert.Equal("--prompt-interactive", c.Args[0]);
        Assert.Contains(@"C:\h\chat.md", c.Args[1]);
        var md = CliHandoff.Markdown("Trip", [U("1", "plan my trip"), A("2", "Sure")]);
        Assert.Contains("# Trip", md);
        Assert.Contains("## User", md);
        Assert.Contains("plan my trip", md);
    }

    // ---- Settings

    [Theory]
    [InlineData(BackendType.ClaudeCode, true, false, false)]
    [InlineData(BackendType.ClaudeCode, true, true, true)]
    [InlineData(BackendType.Antigravity, false, false, true)]
    [InlineData(BackendType.Anthropic, true, false, true)]
    [InlineData(BackendType.Local, true, false, true)]
    public void Hotline_keeps_the_transcript_for_api_chats_or_when_asked(BackendType type, bool keepCli, bool keepInHotline, bool expected) =>
        Assert.Equal(expected, ConnectionTypes.HotlineKeepsTranscript(new BackendProfile { Type = type, KeepCliSessions = keepCli, KeepInHotline = keepInHotline }));

    [Fact]
    public void New_cli_connections_keep_chats_in_the_cli_and_only_a_record_in_hotline()
    {
        var p = new BackendProfile { Type = BackendType.ClaudeCode };
        Assert.True(p.KeepCliSessions);
        Assert.False(p.KeepInHotline);
    }

    [Fact]
    public void Schema_10_cli_connections_switch_to_cli_history()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName),
            """{ "schemaVersion": 10, "chat": { "defaultBackend": "cc", "backends": [ { "id": "cc", "type": "claudeCode", "name": "Claude", "keepCliSessions": false } ] } }""");
        var s = new SettingsStore(_dir).Load();
        Assert.True(Assert.Single(s.Chat.Backends, b => b.Id == "cc").KeepCliSessions);
    }
}
