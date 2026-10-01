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

    private AgyBackend New(string? exe = @"C:\agy.exe") =>
        new(new BackendProfile { Id = "agy", Name = "Gemini (Antigravity)", Agent = "hotline" }, () => exe,
            new AgyWorkspace(Path.Combine(_dir, "ws"), new ManualTimeProvider()), _factory, new FileLog(Path.Combine(_dir, "h.log")));

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
}
