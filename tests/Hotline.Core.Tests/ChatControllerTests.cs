using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;

namespace Hotline.Core.Tests;

public sealed class ChatControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly List<ChatEvent> _events = [];
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private ChatController New(IChatBackend? backend, HistoryStore? history = null)
    {
        var c = new ChatController(id => id == "fake" ? backend : null, history, new ManualTimeProvider(), new FileLog(Path.Combine(_dir, "h.log")))
        { BackendId = "fake" };
        c.Event += _events.Add;
        return c;
    }

    [Fact]
    public async Task Send_streams_deltas_and_records_both_messages()
    {
        var c = New(new FakeBackend(new ChatDelta("Hel"), new ChatDelta("lo")));
        await c.SendAsync("  hi  ", []);

        Assert.Collection(_events,
            e => Assert.Equal("hi", Assert.IsType<UserMessageAdded>(e).Message.Text),
            e => Assert.Equal("Fake", Assert.IsType<AssistantStarted>(e).BackendName),
            e => Assert.Equal("Hel", Assert.IsType<AssistantDelta>(e).Text),
            e => Assert.Equal("lo", Assert.IsType<AssistantDelta>(e).Text),
            e => Assert.IsType<AssistantCompleted>(e));
        Assert.Equal([ChatRole.User, ChatRole.Assistant], c.Messages.Select(m => m.Role));
        Assert.Equal("Hello", c.Messages[1].Text);
        Assert.Equal("fake", c.Messages[1].BackendId);
        Assert.False(c.IsBusy);
    }

    [Fact]
    public async Task Reset_delta_replaces_text()
    {
        var c = New(new FakeBackend(new ChatDelta("draft"), new ChatDelta("final", ResetBefore: true)));
        await c.SendAsync("q", []);
        Assert.Contains(_events, e => e is AssistantDelta { Text: "final", Replace: true });
        Assert.Equal("final", c.Messages[1].Text);
    }

    [Fact]
    public async Task Empty_send_does_nothing()
    {
        var c = New(new FakeBackend(new ChatDelta("x")));
        await c.SendAsync("   ", []);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task Backend_failure_reports_kind_and_drops_user_message_from_context()
    {
        var c = New(new FakeBackend { Throw = new BackendException(BackendErrorKind.NotLoggedIn, "sign in") });
        await c.SendAsync("q", []);
        var failed = Assert.IsType<AssistantFailed>(_events[^1]);
        Assert.Equal(BackendErrorKind.NotLoggedIn, failed.Kind);
        Assert.Empty(c.Messages);
    }

    [Fact]
    public async Task Retry_resends_last_failed_message_without_duplicating_it()
    {
        var calls = 0;
        IChatBackend backend = new FakeBackend { Throw = new BackendException(BackendErrorKind.Failed, "boom") };
        var c = new ChatController(_ => calls++ == 0 ? backend : new FakeBackend(new ChatDelta("ok")), null,
            new ManualTimeProvider(), new FileLog(Path.Combine(_dir, "h.log"))) { BackendId = "fake" };
        c.Event += _events.Add;
        await c.SendAsync("q", []);
        _events.Clear();
        await c.RetryAsync();
        Assert.DoesNotContain(_events, e => e is UserMessageAdded);
        Assert.Equal("q", c.Messages[0].Text);
        Assert.Equal("ok", c.Messages[1].Text);
    }

    [Fact]
    public async Task Unknown_backend_fails_with_not_configured()
    {
        var c = New(null);
        await c.SendAsync("q", []);
        Assert.Equal(BackendErrorKind.NotConfigured, Assert.IsType<AssistantFailed>(_events[^1]).Kind);
    }

    [Fact]
    public async Task Cancel_keeps_partial_reply()
    {
        var gate = new TaskCompletionSource();
        var c = New(new FakeBackend(new ChatDelta("part"), new ChatDelta("never")) { Gate = gate });
        var send = c.SendAsync("q", []);
        c.Cancel();
        await send;
        Assert.IsType<AssistantCancelled>(_events[^1]);
        Assert.Equal("part", c.Messages[1].Text);
    }

    [Fact]
    public async Task Second_send_while_busy_is_ignored()
    {
        var gate = new TaskCompletionSource();
        var c = New(new FakeBackend(new ChatDelta("a"), new ChatDelta("b")) { Gate = gate });
        var first = c.SendAsync("one", []);
        await c.SendAsync("two", []);
        gate.SetResult();
        await first;
        Assert.Single(_events.OfType<UserMessageAdded>());
    }

    [Fact]
    public async Task New_chat_during_streaming_does_not_leak_into_new_conversation()
    {
        var gate = new TaskCompletionSource();
        var c = New(new FakeBackend(new ChatDelta("old"), new ChatDelta("more")) { Gate = gate });
        var send = c.SendAsync("q", []);
        var oldId = c.ConversationId;
        c.NewChat();
        await send;
        Assert.NotEqual(oldId, c.ConversationId);
        Assert.Empty(c.Messages);
        Assert.Contains(_events, e => e is ConversationReset);
    }

    [Fact]
    public void Images_rejected_when_backend_cannot_read_them()
    {
        var c = New(new FakeBackend { Capabilities = new(Images: false, TextFiles: true) });
        var ok = c.CanAccept([new Attachment("1", "a.png", AttachmentKind.Image, "image/png", [1])], out var reason);
        Assert.False(ok);
        Assert.Contains("images", reason);
    }

    [Fact]
    public async Task Completed_turn_is_written_to_history()
    {
        var history = new HistoryStore(Path.Combine(_dir, "history"), new ManualTimeProvider());
        var c = New(new FakeBackend(new ChatDelta("answer")), history);
        await c.SendAsync("question", []);
        Assert.Equal(["question", "answer"], history.Load(c.ConversationId).Select(e => e.Text));
    }
}
