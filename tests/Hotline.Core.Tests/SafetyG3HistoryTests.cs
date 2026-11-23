using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;

namespace Hotline.Core.Tests;

/// <summary>History records that are valid JSON but damaged must not break listing or Resume: they are normalised or skipped one by one.</summary>
public sealed class SafetyG3HistoryTests : TempDirTest
{

    private HistoryStore Store() => new(_dir, new ManualTimeProvider());

    private void Write(string name, params string[] lines)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, name), string.Join("\n", lines) + "\n");
    }

    private const string NoAttachments = """{"id":"m1","role":"user","text":"hello there","at":"2026-10-31T10:00:00+00:00"}""";

    [Fact]
    public void A_record_without_attachments_loads_with_an_empty_list()
    {
        Write("c1.jsonl", NoAttachments);
        var entry = Assert.Single(Store().Load("c1"));
        Assert.NotNull(entry.Attachments);
        Assert.Empty(entry.Attachments);
    }

    [Fact]
    public void Records_with_a_missing_text_an_unknown_role_or_a_null_attachment_entry_are_normalised_or_skipped_individually()
    {
        Write("c1.jsonl",
            NoAttachments,
            """{"id":"m2","role":"user","at":"2026-10-31T10:00:00+00:00"}""",                                 // no text: skipped
            """{"id":"m3","role":"wizard","text":"x","at":"2026-10-31T10:00:00+00:00"}""",                    // unknown role: skipped
            """{"id":"m4","role":"assistant","text":"fine","attachments":[null,{"name":"a.png"}],"at":"2026-10-31T10:00:00+00:00"}""", // junk attachments dropped
            """[1,2,3]""",
            "\"just a string\"");
        var entries = Store().Load("c1");
        Assert.Equal(["m1", "m4"], entries.Select(e => e.Id));
        Assert.Empty(entries[1].Attachments);
    }

    [Fact]
    public void Listing_survives_a_damaged_record_and_still_shows_the_others()
    {
        Write("good.jsonl", NoAttachments);
        Write("damaged.jsonl", """{"id":"m1","role":"user","text":"from a damaged chat"}""");
        Write("metaless.meta.json", """{"lastAt":"2026-10-31T10:00:00+00:00","messageCount":3}"""); // a record with no title
        Write("broken.meta.json", """{"title": 5}""");
        var recent = Store().Recent(10);
        Assert.Contains(recent, r => r.Id == "good" && r.Title == "hello there");
        Assert.Contains(recent, r => r.Id == "damaged");
        Assert.Contains(recent, r => r.Id == "metaless" && r.Title.Length > 0); // the "(attachments)" fallback, not a crash
        Assert.DoesNotContain(recent, r => r.Id == "broken");
    }

    [Fact]
    public void A_record_without_a_title_loads_with_an_empty_one()
    {
        Write("m.meta.json", """{"lastAt":"2026-10-31T10:00:00+00:00","messageCount":3}""");
        Assert.Equal("", Store().LoadMeta("m")!.Title);
        Write("bad.meta.json", "{ not json");
        Assert.Null(Store().LoadMeta("bad"));
    }

    [Fact]
    public void Resume_after_the_current_chat_was_cleared_works_with_damaged_records()
    {
        Write("old.jsonl", NoAttachments);
        Write("old.meta.json", """{"lastAt":"2026-10-31T10:00:00+00:00","messageCount":1}""");
        var c = new ChatController(id => id == "fake" ? new FakeBackend(new ChatDelta("ok")) : null, Store(), new ManualTimeProvider(),
            new FileLog(Path.Combine(_dir, "h.log"))) { BackendId = "fake" };
        var events = new List<ChatEvent>();
        c.Event += events.Add;
        Assert.True(c.Resume("old"));
        Assert.Equal("hello there", Assert.Single(c.Messages).Text);
        Assert.Equal("hello there", Assert.Single(events.OfType<ConversationResumed>()).Title); // a record without a title: the first message
    }
}
