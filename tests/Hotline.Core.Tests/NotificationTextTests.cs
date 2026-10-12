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
}
