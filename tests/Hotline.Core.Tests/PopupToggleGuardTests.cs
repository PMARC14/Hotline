using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class PopupToggleGuardTests
{
    private readonly ManualTimeProvider _clock = new();
    private PopupToggleGuard New() => new(_clock, TimeSpan.FromMilliseconds(300));

    [Fact]
    public void Hidden_popup_is_shown_on_toggle()
        => Assert.True(New().ShouldShowOnToggle(isVisible: false));

    [Fact]
    public void Visible_popup_is_hidden_on_toggle()
        => Assert.False(New().ShouldShowOnToggle(isVisible: true));

    [Fact]
    public void Popup_hidden_by_focus_loss_just_now_stays_hidden()
    {
        var g = New();
        g.NoteHidden();                                    // click on tray stole focus → popup hid itself
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.False(g.ShouldShowOnToggle(isVisible: false));
    }

    [Fact]
    public void Popup_hidden_long_ago_is_shown()
    {
        var g = New();
        g.NoteHidden();
        _clock.Advance(TimeSpan.FromMilliseconds(301));
        Assert.True(g.ShouldShowOnToggle(isVisible: false));
    }
}
