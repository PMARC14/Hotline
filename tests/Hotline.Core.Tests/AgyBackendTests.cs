using System.Text.Json;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class AgyBackendTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeLineProcessFactory _factory = new();
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static ChatMessage U(string id, string text, params Attachment[] a) => new(id, ChatRole.User, text, a, Now);
    private static ChatMessage A(string id, string text) => new(id, ChatRole.Assistant, text, [], Now);

    private static IEnumerable<string> Answer(string text) =>
    [
        """{"event":"init","init":{"tools":[]}}""",
        "{\"event\":\"step_update\",\"step_update\":{\"step_index\":1,\"state\":\"DONE\",\"step_type\":\"agent_response\",\"text_delta\":"
            + JsonSerializer.Serialize(text) + "}}",
        "{\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"response\":" + JsonSerializer.Serialize(text) + "}}",
    ];

    private static string PromptOf(string line) =>
        JsonDocument.Parse(line).RootElement.GetProperty("message").GetProperty("content").GetString()!;

    private AgyBackend New(string? exe = @"C:\agy.exe", BackendProfile? profile = null, string? home = null) =>
        new(profile ?? new BackendProfile { Id = "agy", Name = "Gemini (Antigravity)", Agent = "hotline" }, () => exe,
            new AgyWorkspace(Path.Combine(_dir, "ws"), new ManualTimeProvider()), _factory, new FileLog(Path.Combine(_dir, "h.log")),
            _ => "SYSTEM PROMPT", home ?? Path.Combine(_dir, "home"));

    private static async Task<string> Collect(IAsyncEnumerable<ChatDelta> s)
    {
        var text = "";
        await foreach (var d in s) text = d.ResetBefore ? d.Text : text + d.Text;
        return text;
    }

    [Fact]
    public async Task Missing_exe_is_NotInstalled()
    {
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New(exe: null).StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.NotInstalled, ex.Kind);
        Assert.Empty(_factory.Started);
    }

    [Fact]
    public async Task First_turn_starts_agy_in_workspace_and_streams_answer()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("Hello!") };
        var text = await Collect(New().StreamAsync([U("1", "hi")], default));
        Assert.Equal("Hello!", text);
        var (exe, args, cwd, proc) = Assert.Single(_factory.Started);
        Assert.Equal(@"C:\agy.exe", exe);
        Assert.Contains("stream-json", args);
        Assert.EndsWith("ws", cwd);
        Assert.Equal("hi", PromptOf(Assert.Single(proc.Written)));
        Assert.True(File.Exists(Path.Combine(cwd, ".agents", "agents", "hotline.md")));
    }

    [Fact]
    public async Task Follow_up_reuses_process_without_replaying_context()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var b = New();
        await Collect(b.StreamAsync([U("1", "first")], default));
        await Collect(b.StreamAsync([U("1", "first"), A("2", "ok"), U("3", "second")], default));
        var proc = Assert.Single(_factory.Started).Process;
        Assert.Equal("second", PromptOf(proc.Written[1]));
    }

    [Fact]
    public async Task Cancel_kills_process_and_next_turn_replays_context()
    {
        _factory.Create = () => new FakeLineProcess { Respond = l => PromptOf(l).Contains("slow") ? [] : Answer("ok") };
        var b = New();
        await Collect(b.StreamAsync([U("1", "remember PINEAPPLE")], default));
        using var cts = new CancellationTokenSource();
        var slow = Collect(b.StreamAsync([U("1", "remember PINEAPPLE"), A("2", "ok"), U("3", "slow question")], cts.Token));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow);
        Assert.True(_factory.Started[0].Process.Disposed);

        await Collect(b.StreamAsync([U("1", "remember PINEAPPLE"), A("2", "ok"), U("4", "which word?")], default));

        Assert.Equal(2, _factory.Started.Count);
        var replayed = PromptOf(Assert.Single(_factory.Started[1].Process.Written));
        Assert.Contains("User: remember PINEAPPLE", replayed);
        Assert.EndsWith("which word?", replayed);
    }

    [Fact]
    public async Task New_conversation_restarts_process()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var b = New();
        await Collect(b.StreamAsync([U("1", "a")], default));
        await Collect(b.StreamAsync([U("9", "fresh chat")], default));
        Assert.Equal(2, _factory.Started.Count);
        Assert.True(_factory.Started[0].Process.Disposed);
        Assert.Equal("fresh chat", PromptOf(_factory.Started[1].Process.Written[0]));
    }

    [Fact]
    public async Task Process_exit_mid_turn_fails_with_stderr_tail()
    {
        _factory.Create = () =>
        {
            var p = new FakeLineProcess { StandardErrorTail = "panic: boom" };
            p.Respond = _ => { p.Exit(); return []; };
            return p;
        };
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New().StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.Failed, ex.Kind);
        Assert.Contains("panic: boom", ex.Message);
    }

    [Fact]
    public async Task Error_result_is_mapped()
    {
        _factory.Create = () => new FakeLineProcess
        {
            Respond = _ => ["""{"event":"result","result":{"status":"ERROR","response":"","error":"UNAUTHENTICATED"}}"""],
        };
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New().StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.NotLoggedIn, ex.Kind);
    }

    [Fact]
    public async Task Images_are_saved_in_workspace_and_text_files_inlined()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("7391") };
        var img = new Attachment("a1", "num.png", AttachmentKind.Image, "image/png", [137, 80, 78, 71]);
        var txt = new Attachment("a2", "notes.md", AttachmentKind.Text, "text/plain", "# hi"u8.ToArray());
        await Collect(New().StreamAsync([U("m1", "what number?", img, txt)], default));

        var prompt = PromptOf(_factory.Started[0].Process.Written[0]);
        Assert.Contains("attachments/m1/num.png", prompt);
        Assert.Contains("# hi", prompt);
        Assert.True(File.Exists(Path.Combine(_factory.Started[0].Cwd, "attachments", "m1", "num.png")));
    }

    [Fact]
    public async Task Dispose_stops_process()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var b = New();
        await Collect(b.StreamAsync([U("1", "a")], default));
        await b.DisposeAsync();
        Assert.True(_factory.Started[0].Process.Disposed);
    }

    [Fact]
    public async Task Failed_turn_does_not_leak_into_next_conversation()
    {
        var calls = 0;
        _factory.Create = () => new FakeLineProcess
        {
            Respond = _ => calls++ == 0
                ? ["""{"event":"result","result":{"status":"ERROR","response":"","error":"model overloaded"}}"""]
                : Answer("ok"),
        };
        var b = New();
        await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([U("1", "secret question")], default)));
        await Collect(b.StreamAsync([U("9", "fresh chat")], default));
        Assert.Equal(2, _factory.Started.Count); // fresh process: the failed prompt isn't in its memory
    }

    [Fact]
    public async Task Crash_stderr_is_mapped_to_specific_error()
    {
        _factory.Create = () =>
        {
            var p = new FakeLineProcess();
            p.Respond = _ => { p.StandardErrorTail = "error: UNAUTHENTICATED: sign in required"; p.Exit(); return []; };
            return p;
        };
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New().StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.NotLoggedIn, ex.Kind);
    }

    [Fact]
    public async Task Chat_only_writes_prompt_into_agent_file()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        await Collect(New().StreamAsync([U("1", "hi")], default));
        Assert.Contains("SYSTEM PROMPT", File.ReadAllText(Path.Combine(_dir, "ws", ".agents", "agents", "hotline.md")));
    }

    [Fact]
    public async Task Inherit_mode_uses_working_dir_and_prefixes_prompt()
    {
        var work = Directory.CreateDirectory(Path.Combine(_dir, "project")).FullName;
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var img = new Attachment("a1", "shot.png", AttachmentKind.Image, "image/png", [1, 2]);
        var b = New(profile: new BackendProfile { Id = "agy", Name = "G", Tools = ToolMode.Inherit, WorkingDirectory = work });
        await Collect(b.StreamAsync([U("m1", "look", img)], default));

        var (_, args, cwd, proc) = _factory.Started[0];
        Assert.Equal(work, cwd);
        Assert.DoesNotContain("--agent", args);
        var prompt = PromptOf(proc.Written[0]);
        Assert.StartsWith("Instructions for this conversation:", prompt);
        Assert.Contains(Path.Combine(_dir, "ws", "attachments", "m1", "shot.png"), prompt); // absolute path
    }

    [Fact]
    public async Task Inherit_missing_working_dir_is_an_error_not_the_home_folder()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var b = New(profile: new BackendProfile { Id = "agy", Name = "G", Tools = ToolMode.Inherit, WorkingDirectory = @"Z:\does\not\exist" });
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.NotConfigured, ex.Kind);
        Assert.Empty(_factory.Started);
    }

    [Fact]
    public async Task Inherit_without_folder_uses_home()
    {
        var home = Directory.CreateDirectory(Path.Combine(_dir, "home")).FullName;
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        await Collect(New(profile: new BackendProfile { Id = "agy", Name = "G", Tools = ToolMode.Inherit }, home: home).StreamAsync([U("1", "hi")], default));
        Assert.Equal(home, _factory.Started[0].Cwd);
    }

    [Fact]
    public async Task Edited_system_prompt_restarts_the_session()
    {
        var prompt = "first";
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var b = new AgyBackend(new BackendProfile { Id = "agy", Name = "G", Agent = "hotline" }, () => @"C:\agy.exe",
            new AgyWorkspace(Path.Combine(_dir, "ws"), new ManualTimeProvider()), _factory, new FileLog(Path.Combine(_dir, "h.log")), _ => prompt, _dir);
        await Collect(b.StreamAsync([U("1", "hi")], default));
        prompt = "second";
        await Collect(b.StreamAsync([U("1", "hi"), new ChatMessage("2", ChatRole.Assistant, "ok", [], Now), U("3", "again")], default));
        Assert.Equal(2, _factory.Started.Count);
        Assert.Contains("second", File.ReadAllText(Path.Combine(_dir, "ws", ".agents", "agents", "hotline.md")));
    }
}
