using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;

namespace Hotline.Core.Tests;

public sealed class RecentChatsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _clock = new();
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private HistoryStore Store() => new(Path.Combine(_dir, "history"), _clock);

    private ChatMessage Msg(ChatRole role, string text, params Attachment[] a) => new(Ids.New(), role, text, a, _clock.GetUtcNow(), role == ChatRole.Assistant ? "agy" : null);

    [Fact]
    public void Recent_lists_newest_first_with_a_title_from_the_first_question()
    {
        var store = Store();
        store.Append("old", Msg(ChatRole.User, "why is the sky blue"));
        store.Append("old", Msg(ChatRole.Assistant, "Rayleigh scattering."));
        File.SetLastWriteTimeUtc(store.PathFor("old"), DateTime.UtcNow.AddHours(-2));
        store.Append("new", Msg(ChatRole.User, "  render me a complex markdown statement to test rendering, with tables, code, lists and quotes please  "));
        var recent = store.Recent(10);
        Assert.Equal(["new", "old"], recent.Select(r => r.Id));
        Assert.Equal("why is the sky blue", recent[1].Title);
        Assert.Equal(2, recent[1].MessageCount);
        Assert.True(recent[0].Title.Length <= 61 && recent[0].Title.EndsWith('…'));
    }

    [Fact]
    public void Recent_skips_unreadable_or_empty_files_and_respects_count()
    {
        var store = Store();
        for (var i = 0; i < 5; i++) store.Append($"c{i}", Msg(ChatRole.User, $"q{i}"));
        File.WriteAllText(store.PathFor("broken"), "{ not json");
        File.WriteAllText(store.PathFor("empty"), "");
        var recent = store.Recent(3);
        Assert.Equal(3, recent.Count);
        Assert.DoesNotContain(recent, r => r.Id is "broken" or "empty");
    }

    [Fact]
    public async Task Resume_restores_messages_continues_the_same_file_and_replays_context()
    {
        var store = Store();
        store.Append("conv", Msg(ChatRole.User, "first question", new Attachment("a", "shot.png", AttachmentKind.Image, "image/png", [1])));
        store.Append("conv", Msg(ChatRole.Assistant, "first answer"));
        var backend = new FakeBackend(new ChatDelta("ok"));
        var chat = new ChatController(_ => backend, store, _clock, new FileLog(Path.Combine(_dir, "h.log")));
        var events = new List<ChatEvent>();
        chat.Event += events.Add;

        Assert.True(chat.Resume("conv"));
        Assert.Equal("conv", chat.ConversationId);
        Assert.IsType<ConversationReset>(events[0]);
        Assert.Equal(2, events.OfType<MessageRestored>().Count());
        Assert.Contains("[attached: shot.png]", chat.Messages[0].Text);

        await chat.SendAsync("follow up", []);
        Assert.Equal(["first question", "first answer", "follow up"], backend.Calls[0].Select(m => m.Text.Split('\n')[0]));
        Assert.Equal(4, store.Load("conv").Count);
    }

    [Fact]
    public void Resume_of_a_missing_chat_changes_nothing()
    {
        var chat = new ChatController(_ => null, Store(), _clock, new FileLog(Path.Combine(_dir, "h.log")));
        var id = chat.ConversationId;
        Assert.False(chat.Resume("nope"));
        Assert.Equal(id, chat.ConversationId);
    }
}
