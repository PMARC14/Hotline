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
    public void A_damaged_line_skips_only_that_line()
    {
        // A crash mid-append leaves a cut-off last line; a hand edit can leave "null" or blanks.
        var store = new HistoryStore(_dir, _clock);
        store.Append("c1", new ChatMessage("m1", ChatRole.User, "kept", [], _clock.GetUtcNow()));
        File.AppendAllText(store.PathFor("c1"), "null\n   \n{\"id\":\"m2\",\"role\":\"ass");
        Assert.Equal(["kept"], store.Load("c1").Select(e => e.Text));
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

    [Theory]
    [InlineData(@"..\escape")]
    [InlineData(@"C:\escape")]
    [InlineData("a/b")]
    [InlineData("")]
    public void An_unsafe_id_never_touches_the_disk_and_never_throws(string id)
    {
        var store = new HistoryStore(_dir, _clock);
        store.Append(id, new ChatMessage("m", ChatRole.User, "x", [], _clock.GetUtcNow()));
        store.SaveMeta(id, new HistoryStore.Meta("t", _clock.GetUtcNow(), null, null, null, 1));

        Assert.Empty(store.Load(id));
        Assert.Null(store.LoadMeta(id));
        Assert.Throws<ArgumentException>(() => store.PathFor(id));
        Assert.False(Directory.Exists(_dir)); // nothing was created
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_dir)!, "escape.jsonl")));
    }

    [Fact]
    public void A_very_long_id_is_refused()
    {
        var store = new HistoryStore(_dir, _clock);
        store.Append(new string('a', 65), new ChatMessage("m", ChatRole.User, "x", [], _clock.GetUtcNow()));
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Files_with_odd_names_are_not_listed_as_chats()
    {
        var store = new HistoryStore(_dir, _clock);
        store.Append("ok", new ChatMessage("m", ChatRole.User, "x", [], _clock.GetUtcNow()));
        File.WriteAllText(Path.Combine(_dir, "has space.jsonl"), "{\"id\":\"m\",\"role\":\"user\",\"text\":\"y\",\"at\":\"2026-01-01T00:00:00Z\"}\n");
        Assert.Equal(["ok"], store.Recent(10).Select(s => s.Id));
    }
}
