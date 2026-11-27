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

    [Fact]
    public void Older_chats_with_a_plain_fence_still_unwrap()
        => Assert.Equal("summarize\n[attached: notes.txt]", CliTranscripts.Unwrap("Attached file notes.txt:\n```\nsecret\n```\n\nsummarize"));
}
