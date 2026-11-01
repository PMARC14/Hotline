using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public class ScrollFollowTests
{
    [Fact]
    public void An_auto_scroll_that_ends_short_of_the_bottom_scrolls_again_instead_of_stopping()
    {
        var f = new ScrollFollow();
        Assert.True(f.Settled(atBottom: false)); // the answer grew while the view was scrolling: go again
        Assert.True(f.Following);
    }

    [Fact]
    public void Scrolling_up_stops_following_until_back_at_the_bottom()
    {
        var f = new ScrollFollow();
        f.UserInput();
        Assert.False(f.Settled(atBottom: false));
        Assert.False(f.Following);
        Assert.False(f.Settled(atBottom: false)); // the answer keeps growing below: leave the reader alone
        f.UserInput();
        Assert.False(f.Settled(atBottom: true)); // they scrolled back down
        Assert.True(f.Following);
    }

    [Fact]
    public void Scrolling_while_at_the_bottom_keeps_following()
    {
        var f = new ScrollFollow();
        f.UserInput();
        f.Settled(atBottom: true);
        Assert.True(f.Following);
    }

    [Fact]
    public void Sending_a_message_follows_again()
    {
        var f = new ScrollFollow();
        f.UserInput();
        f.Settled(atBottom: false);
        f.Follow();
        Assert.True(f.Following);
    }
}
