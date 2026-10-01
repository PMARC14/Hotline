using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _clock = new();
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Round_trips_messages_with_attachment_metadata_but_no_bytes()
    {
        var store = new HistoryStore(_dir, _clock);
        var att = new Attachment("a1", "shot.png", AttachmentKind.Image, "image/png", new byte[5000]);
        store.Append("c1", new ChatMessage("m1", ChatRole.User, "look", [att], _clock.GetUtcNow()));
        store.Append("c1", new ChatMessage("m2", ChatRole.Assistant, "nice", [], _clock.GetUtcNow(), "agy"));

        var entries = store.Load("c1");

        Assert.Equal(["look", "nice"], entries.Select(e => e.Text));
        Assert.Equal("shot.png", entries[0].Attachments.Single().Name);
        Assert.Equal("agy", entries[1].BackendId);
        Assert.True(new FileInfo(store.PathFor("c1")).Length < 2000);
    }

    [Fact]
    public void Missing_conversation_loads_empty()
        => Assert.Empty(new HistoryStore(_dir, _clock).Load("nope"));

    [Fact]
    public void Prune_deletes_files_older_than_retention()
    {
        var store = new HistoryStore(_dir, _clock);
        store.Append("old", new ChatMessage("m", ChatRole.User, "x", [], _clock.GetUtcNow()));
        store.Append("new", new ChatMessage("m", ChatRole.User, "y", [], _clock.GetUtcNow()));
        File.SetLastWriteTimeUtc(store.PathFor("old"), _clock.GetUtcNow().UtcDateTime.AddDays(-40));

        Assert.Equal(1, store.Prune(retentionDays: 30));
        Assert.False(File.Exists(store.PathFor("old")));
        Assert.True(File.Exists(store.PathFor("new")));
    }
}
