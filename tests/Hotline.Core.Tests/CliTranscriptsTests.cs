using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Settings;
using Microsoft.Data.Sqlite;

namespace Hotline.Core.Tests;

/// <summary>Reopening a CLI chat whose messages Hotline didn't keep: they come from the CLI's own history.</summary>
public sealed class CliTranscriptsTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // release the agy test database before deleting it
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private static string[] Show(IReadOnlyList<HistoryStore.Entry>? entries) =>
        [.. (entries ?? []).Select(e => $"{(e.Role == ChatRole.User ? "U" : "A")}: {e.Text}")];

    // ---- unwrapping what Hotline sent -----------------------------------------------------------

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("<previous_conversation>\n<turn role=\"user\">\nold\n</turn>\n</previous_conversation>\n\nNew message:\nhello", "hello")]
    [InlineData("Conversation so far (for context):\nUser: old\nAssistant: reply\n\nNew message:\nhello", "hello")]
    [InlineData("Instructions for this conversation:\nBe brief.\n\nhello", "hello")]
    [InlineData("Attached file notes.txt:\n```\nsecret contents\n```\n\nsummarize", "summarize\n[attached: notes.txt]")]
    [InlineData("Attached images (in the workspace; use view_file to look at them):\n- C:\\x\\shot.png\n\nwhat is this", "what is this\n[attached: shot.png]")]
    [InlineData("Please look at the attached file(s).", "")]
    [InlineData("Please look at the attachment(s).", "")]
    public void Hotlines_own_wrapping_is_removed(string sent, string shown) => Assert.Equal(shown, CliTranscripts.Unwrap(sent));

    [Fact]
    public void Multi_paragraph_instructions_never_show_in_the_first_message()
    {
        // agy's inherit mode puts the system prompt (with saved memory: several paragraphs) in the first message.
        var sent = Hotline.Core.Backends.Agy.AgyProtocol.ComposePrompt("what's my name?", [], [], [],
            "Be brief.\n\n## Notes the user saved about themselves\n- name: Pat");
        Assert.Equal("what's my name?", CliTranscripts.Unwrap(sent));
    }

    // ---- Claude Code ------------------------------------------------------------------------------

    [Fact]
    public void Claude_code_session_reads_the_conversation()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_home, ".claude", "projects", "C--some-folder")).FullName;
        File.WriteAllLines(Path.Combine(dir, "s1.jsonl"),
        [
            """{"type":"summary","summary":"x"}""",
            """{"type":"user","isMeta":true,"message":{"role":"user","content":"<command-name>/x</command-name>"}}""",
            """{"type":"user","timestamp":"2026-10-15T10:00:00Z","message":{"role":"user","content":"<previous_conversation>\n<turn role=\"user\">\nold\n</turn>\n</previous_conversation>\n\nNew message:\nhello"}}""",
            """{"type":"assistant","timestamp":"2026-10-15T10:00:05Z","message":{"role":"assistant","content":[{"type":"thinking","thinking":"hmm"}]}}""",
            """{"type":"assistant","timestamp":"2026-10-15T10:00:06Z","message":{"role":"assistant","content":[{"type":"text","text":"Hi there"}]}}""",
            """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","content":"ok"}]}}""",
            """{"type":"assistant","isSidechain":true,"message":{"role":"assistant","content":[{"type":"text","text":"subagent"}]}}""",
            """{"type":"user","timestamp":"2026-10-15T10:01:00Z","message":{"role":"user","content":[{"type":"text","text":"and more"},{"type":"image","source":{}}]}}""",
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"Sure."}]}}""",
            """not json""",
            // an attachment-only message keeps its place
            """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"Please look at the attachment(s)."},{"type":"image","source":{}}]}}""",
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"a cat"}]}}""",
            """{"type":"user","message":null}""",
        ]);
        var entries = CliTranscripts.Read(BackendType.ClaudeCode, "s1", _home, "claude");
        Assert.Equal(["U: hello", "A: Hi there", "U: and more\n[attached: image]", "A: Sure.", "U: [attached: image]", "A: a cat"], Show(entries));
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero), entries![0].At);
        Assert.All(entries, e => Assert.Equal("claude", e.BackendId));
    }

    // ---- Codex ------------------------------------------------------------------------------------

    [Fact]
    public void Codex_thread_reads_the_conversation()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_home, ".codex", "sessions", "2026", "10", "15")).FullName;
        File.WriteAllLines(Path.Combine(dir, "rollout-2026-10-15T10-00-00-t1.jsonl"),
        [
            """{"timestamp":"2026-10-15T10:00:00Z","type":"session_meta","payload":{"id":"t1"}}""",
            """{"timestamp":"2026-10-15T10:00:00Z","type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"rules"}]}}""",
            """{"timestamp":"2026-10-15T10:00:00Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"<environment_context>cwd</environment_context>"}]}}""",
            """{"timestamp":"2026-10-15T10:00:01Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"hello"}]}}""",
            """{"timestamp":"2026-10-15T10:00:02Z","type":"response_item","payload":{"type":"reasoning","summary":[]}}""",
            """{"timestamp":"2026-10-15T10:00:03Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Hi"}]}}""",
            """{"timestamp":"2026-10-15T10:00:04Z","type":"event_msg","payload":{"type":"agent_message","message":"Hi"}}""",
            // Hotline's wrapped turns are unwrapped; a message that merely starts with a tag is kept, an injected one is not
            """{"type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"<previous_conversation>\n<turn role=\"user\">\nold\n</turn>\n</previous_conversation>\n\nNew message:\nagain"}]}}""",
            """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"<div>html answer</div>"}]}}""",
            """{"type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"<user_instructions>rules</user_instructions>"}]}}""",
        ]);
        Assert.Equal(["U: hello", "A: Hi", "U: again", "A: <div>html answer</div>"], Show(CliTranscripts.Read(BackendType.Codex, "t1", _home, "codex")));
    }

    // ---- agy --------------------------------------------------------------------------------------

    private static byte[] Varint(ulong v)
    {
        var bytes = new List<byte>();
        do { var b = (byte)(v & 0x7F); v >>= 7; bytes.Add(v != 0 ? (byte)(b | 0x80) : b); } while (v != 0);
        return [.. bytes];
    }
    private static byte[] Field(int number, byte[] value) => [.. Varint((ulong)(number << 3 | 2)), .. Varint((ulong)value.Length), .. value];
    private static byte[] Text(int number, string s) => Field(number, System.Text.Encoding.UTF8.GetBytes(s));

    private string AgyDb(string id, params (int Type, byte[] Payload)[] steps)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_home, ".gemini", "antigravity-cli", "conversations")).FullName;
        var path = Path.Combine(dir, id + ".db");
        using var db = new SqliteConnection($"Data Source={path}");
        db.Open();
        using (var create = db.CreateCommand())
        {
            create.CommandText = "CREATE TABLE steps (idx integer, step_type integer, status integer, step_payload blob, PRIMARY KEY (idx))";
            create.ExecuteNonQuery();
        }
        for (var i = 0; i < steps.Length; i++)
        {
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO steps VALUES ($i, $t, 3, $p)";
            insert.Parameters.AddWithValue("$i", i);
            insert.Parameters.AddWithValue("$t", steps[i].Type);
            insert.Parameters.AddWithValue("$p", steps[i].Payload);
            insert.ExecuteNonQuery();
        }
        return path;
    }

    [Fact]
    public void Agy_conversation_reads_the_user_and_answer_steps()
    {
        var other = Text(5, "metadata-id-1234567890");
        AgyDb("c1",
            (14, [.. other, .. Field(19, [.. Text(2, "give me a test"), .. Text(3, "x")])]),
            (15, [.. other, .. Field(20, [.. Text(1, "Here is a test."), .. Text(8, "Here is a test.")])]),
            (101, Field(114, Text(1, "[Message] system notice"))),
            (14, Field(19, Text(2, "Conversation so far (for context):\nUser: a\n\nNew message:\nthanks"))),
            (15, Field(20, Text(1, "You're welcome!"))));
        Assert.Equal(["U: give me a test", "A: Here is a test.", "U: thanks", "A: You're welcome!"],
            Show(CliTranscripts.Read(BackendType.Antigravity, "c1", _home, "agy")));
    }

    [Fact]
    public void A_malformed_agy_step_is_skipped_not_a_crash()
    {
        byte[] huge = [.. Varint(19 << 3 | 2), 0xFF, 0xFF, 0xFF, 0xFF, 0x07]; // length ~2^31: past the end
        byte[] truncated = [.. Varint(5 << 3 | 1), 1, 2]; // a fixed64 with 2 of its 8 bytes
        AgyDb("bad", (14, huge), (14, truncated), (15, Field(20, Text(1, "still here"))));
        Assert.Equal(["A: still here"], Show(CliTranscripts.Read(BackendType.Antigravity, "bad", _home, "agy")));
    }

    [Fact]
    public void A_missing_or_unreadable_store_gives_nothing()
    {
        Assert.Null(CliTranscripts.Read(BackendType.ClaudeCode, "nope", _home, "claude"));
        Assert.Null(CliTranscripts.Read(BackendType.Codex, "nope", _home, "codex"));
        Assert.Null(CliTranscripts.Read(BackendType.Antigravity, "nope", _home, "agy"));
        var dir = Directory.CreateDirectory(Path.Combine(_home, ".gemini", "antigravity-cli", "conversations")).FullName;
        File.WriteAllText(Path.Combine(dir, "broken.db"), "not a database");
        Assert.Null(CliTranscripts.Read(BackendType.Antigravity, "broken", _home, "agy"));
        Assert.Null(CliTranscripts.Read(BackendType.Anthropic, "x", _home, "claude-api")); // API chats have no CLI store
    }

    [Theory]
    [InlineData("..\\..\\escape")]
    [InlineData("a/b")]
    [InlineData("")]
    public void Session_ids_that_are_not_plain_names_are_refused(string id)
        => Assert.Null(CliTranscripts.Read(BackendType.ClaudeCode, id, _home, "claude"));
}
