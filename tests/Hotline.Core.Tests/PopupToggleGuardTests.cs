using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class PopupToggleGuardTests
{
    private readonly ManualTimeProvider _clock = new();
    private PopupToggleGuard New() => new(_clock, TimeSpan.FromMilliseconds(300));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Toggle_shows_a_hidden_popup_and_hides_a_visible_one(bool isVisible, bool show)
        => Assert.Equal(show, New().ShouldShowOnToggle(isVisible));

    [Theory]
    [InlineData(50, false)]   // click on tray stole focus, so the popup hid itself just now: stay hidden
    [InlineData(301, true)]   // hidden long ago: show
    public void A_popup_that_just_hid_itself_stays_hidden_for_a_moment(int sinceHiddenMs, bool show)
    {
        var g = New();
        g.NoteHidden();
        _clock.Advance(TimeSpan.FromMilliseconds(sinceHiddenMs));
        Assert.Equal(show, g.ShouldShowOnToggle(isVisible: false));
    }
}
