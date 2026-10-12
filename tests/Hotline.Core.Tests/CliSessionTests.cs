using System.Runtime.CompilerServices;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;

namespace Hotline.Core.Tests;

/// <summary>Chats kept in a CLI's own history: the controller records the session, resumes it and drops it on New chat.</summary>
public sealed class CliSessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly List<ChatEvent> _events = [];
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    /// <summary>Answers "ok" and reports a session: the one it was asked to resume, else a new one.</summary>
    private sealed class FakeCli(string id = "cli") : IChatBackend, ICliSessionBackend
    {
        private int _next;
        public string Id => id;
        public string DisplayName => "Fake CLI";
        public BackendCapabilities Capabilities { get; } = new(Images: true, TextFiles: true);
        public CliSession? Resume { get; set; }
        public List<CliSession?> ResumeSeen { get; } = [];

        public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
        {
            ResumeSeen.Add(Resume);
            await Task.Yield();
            yield return new ChatDelta("", Session: Resume ?? new CliSession($"session-{++_next:D4}", @"C:\work\Hotline"));
            yield return new ChatDelta("ok");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private HistoryStore History() => new(Path.Combine(_dir, "history"), new ManualTimeProvider());

    private ChatController New(IChatBackend backend, HistoryStore? history, bool keepTranscript = true)
    {
        var c = new ChatController(id => id == backend.Id ? backend : null, history, new ManualTimeProvider(), new FileLog(Path.Combine(_dir, "h.log")),
            _ => keepTranscript) { BackendId = backend.Id };
        c.Event += _events.Add;
        return c;
    }

    [Fact]
    public async Task The_reported_session_is_resumed_on_the_next_turn_and_recorded()
    {
        var cli = new FakeCli();
        var history = History();
        var c = New(cli, history);
        await c.SendAsync("plan my trip", []);
        await c.SendAsync("and the hotel?", []);

        Assert.Null(cli.ResumeSeen[0]);
        Assert.Equal("session-0001", cli.ResumeSeen[1]?.Id);
        Assert.DoesNotContain(_events, e => e is AssistantDelta { Text: "" }); // the session note isn't answer text
        var meta = history.LoadMeta(c.ConversationId)!;
        Assert.Equal(("plan my trip", "cli", "session-0001", @"C:\work\Hotline", 4), (meta.Title, meta.BackendId, meta.SessionId, meta.WorkingDirectory, meta.MessageCount));
    }

    [Fact]
    public async Task New_chat_starts_a_new_cli_conversation()
    {
        var cli = new FakeCli();
        var c = New(cli, History());
        await c.SendAsync("one", []);
        c.NewChat();
        await c.SendAsync("two", []);
        Assert.Null(cli.ResumeSeen[1]);
        Assert.Null(c.CurrentSession is { } s && s.Session.Id == "session-0001" ? s : null);
    }

    [Fact]
    public async Task Without_keep_in_hotline_only_the_record_is_kept_and_recent_lists_it()
    {
        var history = History();
        var c = New(new FakeCli(), history, keepTranscript: false);
        await c.SendAsync("what's the weather on Mars", []);

        Assert.Empty(history.Load(c.ConversationId));
        var recent = Assert.Single(history.Recent(10));
        Assert.Equal(("what's the weather on Mars", false, "cli", 2), (recent.Title, recent.HasTranscript, recent.BackendId, recent.MessageCount));
    }

    [Fact]
    public async Task A_record_only_chat_resumes_its_cli_conversation_with_an_empty_panel()
    {
        var history = History();
        var first = New(new FakeCli(), history, keepTranscript: false);
        await first.SendAsync("remember pelican", []);
        var id = first.ConversationId;

        var cli = new FakeCli();
        _events.Clear();
        var c = New(cli, history, keepTranscript: false);
        Assert.True(c.Resume(id));
        Assert.Empty(c.Messages);
        var resumed = Assert.Single(_events.OfType<ConversationResumed>());
        Assert.Equal(("remember pelican", "cli", false), (resumed.Title, resumed.BackendId, resumed.HasTranscript));

        await c.SendAsync("what word?", []);
        Assert.Equal(new CliSession("session-0001", @"C:\work\Hotline"), cli.ResumeSeen.Single());
        Assert.Equal(4, history.LoadMeta(id)!.MessageCount);
    }

    [Fact]
    public async Task Resuming_switches_to_the_connection_that_holds_the_conversation()
    {
        var history = History();
        var cli = new FakeCli("claude");
        var other = new FakeCli("api");
        var c = new ChatController(id => id == "claude" ? cli : id == "api" ? other : null, history, new ManualTimeProvider(),
            new FileLog(Path.Combine(_dir, "h.log"))) { BackendId = "claude" };
        await c.SendAsync("hi", []);
        var id = c.ConversationId;
        c.NewChat();
        c.BackendId = "api";
        Assert.True(c.Resume(id));
        Assert.Equal("claude", c.BackendId);
    }

    [Fact]
    public async Task Another_connection_never_gets_this_chats_session()
    {
        var cli = new FakeCli("claude");
        var other = new FakeCli("agy");
        var c = new ChatController(id => id == "claude" ? cli : id == "agy" ? other : null, History(), new ManualTimeProvider(),
            new FileLog(Path.Combine(_dir, "h.log"))) { BackendId = "claude" };
        await c.SendAsync("hi", []);
        c.BackendId = "agy";
        await c.SendAsync("and you?", []);
        Assert.Null(other.ResumeSeen.Single());
    }

    [Fact]
    public void A_record_with_a_bad_session_id_is_not_resumable()
    {
        var history = History();
        history.SaveMeta("abc", new HistoryStore.Meta("t", DateTimeOffset.UnixEpoch, "cli", "x; rm -rf /", @"C:\w", 2));
        Assert.Null(history.LoadMeta("abc")!.SessionId);
        Assert.False(New(new FakeCli(), history).Resume("abc"));
    }

    [Fact]
    public async Task A_chat_reopened_from_a_record_is_partial_until_a_new_chat()
    {
        var history = History();
        var first = New(new FakeCli(), history, keepTranscript: false);
        await first.SendAsync("remember pelican", []);
        var c = New(new FakeCli(), history, keepTranscript: false);
        Assert.True(c.HasWholeTranscript);
        c.Resume(first.ConversationId);
        Assert.False(c.HasWholeTranscript);
        c.NewChat();
        Assert.True(c.HasWholeTranscript);
    }

    [Fact]
    public void A_chats_transcript_and_record_expire_together()
    {
        var clock = new ManualTimeProvider();
        var dir = Path.Combine(_dir, "history");
        var history = new HistoryStore(dir, clock);
        history.Append("old", new ChatMessage("1", ChatRole.User, "hi", [], DateTimeOffset.UnixEpoch));
        history.SaveMeta("old", new HistoryStore.Meta("hi", DateTimeOffset.UnixEpoch, "cli", "session-0001", @"C:\w", 1));
        File.SetLastWriteTimeUtc(history.PathFor("old"), clock.GetUtcNow().UtcDateTime.AddDays(-40));
        Assert.Equal(0, history.Prune(30)); // the record is recent: keep both
        File.SetLastWriteTimeUtc(Path.Combine(dir, "old.meta.json"), clock.GetUtcNow().UtcDateTime.AddDays(-40));
        Assert.Equal(2, history.Prune(30));
        Assert.Empty(Directory.EnumerateFiles(dir));
    }

    [Theory]
    [InlineData("6a8b2362-20cc-4208-bd05-cf9b25fe01c9", true)]
    [InlineData("thread_0123456789", true)]
    [InlineData("short", false)]
    [InlineData("--dangerously-skip", false)]
    [InlineData("a b c d e f g h", false)]
    [InlineData(null, false)]
    public void Session_ids_are_plain(string? id, bool valid) => Assert.Equal(valid, CliSession.IsValidId(id));
}
