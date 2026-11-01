using Hotline.Core.Text;

namespace Hotline.Core.Tests;

public class NotificationTextTests
{
    [Fact]
    public void Snippet_drops_markdown_code_and_math()
    {
        var text = "## Answer\n\n**Yes** — use `git rebase`:\n\n```bash\ngit rebase main\n```\n\n- see [the docs](https://git-scm.com)\n$$x^2$$";
        Assert.Equal("Answer Yes — use git rebase: see the docs", NotificationText.Snippet(text));
    }

    [Fact]
    public void Snippet_keeps_underscores_inside_words_but_drops_emphasis()
    {
        Assert.Equal("set notify_when_done in config_file.json, it matters", NotificationText.Snippet("set `notify_when_done` in config_file.json, _it matters_"));
    }

    [Theory]
    [InlineData("aaaaaaaaaa", 4, "aaaa…")]                 // cut at the limit with an ellipsis
    [InlineData("short", 5, "short")]
    [InlineData("ab\U0001F600cd", 3, "ab…")]              // never cuts an emoji in half
    public void Snippet_is_cut_at_the_limit(string text, int max, string expected)
        => Assert.Equal(expected, NotificationText.Snippet(text, max: max));

    [Fact]
    public void Snippet_of_nothing_is_empty_and_drops_control_characters()
    {
        Assert.Equal("", NotificationText.Snippet(""));
        Assert.Equal("a b", NotificationText.Snippet("a\u001b[0m b\u0000"));
    }
}
