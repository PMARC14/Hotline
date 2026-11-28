using Hotline.Core.Chat;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Backends.ClaudeCode;

namespace Hotline.Core.Tests;

/// <summary>
/// An attached text file is sent inside a code fence. A file that itself contains ``` (markdown, a code sample) closed a
/// plain ``` fence early: the rest of the file read as the user's own words, and a reopened chat showed it as typed text.
/// </summary>
public sealed class AttachedFileTextTests
{
    private const string Readme = "# Notes\n```bash\nmake\n```\nafter the sample";

    [Theory]
    [InlineData("plain text", "```")]
    [InlineData("has ``` inside", "````")]
    [InlineData("has ````` five", "``````")]
    [InlineData("one ` and two ``", "```")]
    public void The_fence_is_longer_than_any_backtick_run_in_the_file(string content, string fence)
        => Assert.Equal($"Attached file a.md:\n{fence}\n{content}\n{fence}\n\n", AttachedFileText.Wrap("a.md", content));

    [Fact]
    public void A_file_with_its_own_code_fence_stays_inside_the_wrapper_for_every_backend()
    {
        string[] sent =
        [
            AgyProtocol.ComposePrompt("summarize", [], [("readme.md", Readme)], []),
            ClaudeCodeProtocol.ComposeText("summarize", [("readme.md", Readme)], []),
        ];
        foreach (var s in sent)
        {
            Assert.Contains(AttachedFileText.Wrap("readme.md", Readme).TrimEnd('\n'), s.ReplaceLineEndings("\n"));
            Assert.Equal("summarize\n[attached: readme.md]", CliTranscripts.Unwrap(s));
        }
    }

    [Theory]
    [InlineData("")]                                   // empty file
    [InlineData("ends with backticks ```")]            // content ending in a backtick run
    [InlineData("line 1\r\nline 2\r\n")]               // CRLF content
    [InlineData("Attached file y.md:\n```\nnested\n```")] // a file that looks like another wrapper
    public void Odd_contents_round_trip(string content)
    {
        var sent = AgyProtocol.ComposePrompt("go", [], [("x: (1).md", content)], []);
        Assert.Equal("go\n[attached: x: (1).md]", CliTranscripts.Unwrap(sent));
    }

    [Fact]
    public void Two_files_and_text_with_long_backtick_runs_keep_the_typed_text()
    {
        var sent = ClaudeCodeProtocol.ComposeText("keep ```` this", [("a.md", Readme), ("b.txt", "plain")], []);
        Assert.Equal("keep ```` this\n[attached: a.md]\n[attached: b.txt]", CliTranscripts.Unwrap(sent));
    }

    [Fact]
    public void The_api_backends_wrap_text_files_the_same_way()
    {
        var file = new Attachment("f1", "readme.md", AttachmentKind.Text, "text/markdown", System.Text.Encoding.UTF8.GetBytes(Readme));
        var m = new ChatMessage("1", ChatRole.User, "summarize", [file], DateTimeOffset.UnixEpoch);
        Assert.Equal(AttachedFileText.Wrap("readme.md", Readme) + "summarize", Hotline.Core.Backends.Api.ApiCommon.WithTextFiles(m));
    }

    [Fact]
    public void A_missing_closing_fence_in_a_large_message_is_quick()
    {
        var sent = string.Concat(Enumerable.Repeat("Attached file x:\n````\n" + new string('a', 2_000) + "\n", 500));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _ = CliTranscripts.Unwrap(sent);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    [Fact]
    public void Older_chats_with_a_plain_fence_still_unwrap()
        => Assert.Equal("summarize\n[attached: notes.txt]", CliTranscripts.Unwrap("Attached file notes.txt:\n```\nsecret\n```\n\nsummarize"));
}
