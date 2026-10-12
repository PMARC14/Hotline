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
    public void Snippet_is_cut_at_the_limit_with_an_ellipsis()
    {
        Assert.Equal("aaaa…", NotificationText.Snippet(new string('a', 10), max: 4));
        Assert.Equal("short", NotificationText.Snippet("short", max: 4 + 1));
        Assert.Equal("", NotificationText.Snippet(""));
    }

    [Fact]
    public void Snippet_keeps_underscores_inside_words_but_drops_emphasis()
    {
        Assert.Equal("set notify_when_done in config_file.json, it matters", NotificationText.Snippet("set `notify_when_done` in config_file.json, _it matters_"));
    }

    [Fact]
    public void Snippet_never_cuts_an_emoji_in_half_or_keeps_control_characters()
    {
        Assert.Equal("ab…", NotificationText.Snippet("ab\U0001F600cd", max: 3));
        Assert.Equal("a b", NotificationText.Snippet("a\u001b[0m b\u0000"));
    }
}
