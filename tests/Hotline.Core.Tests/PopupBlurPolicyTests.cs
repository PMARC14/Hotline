using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>What the panel does when it loses focus (moved out of PopupWindow unchanged).</summary>
public sealed class PopupBlurPolicyTests
{
    private const nint Panel = 0x10, Other = 0x20;

    [Theory]
    [InlineData(true, 0, "CopilotKeyPressFocusFrame", false, true)]
    [InlineData(true, 0, "CopilotKeyPressFocusFrame", true, false)] // dictation, or the tap does something else
    [InlineData(true, 1, "CopilotKeyPressFocusFrame", false, false)] // a dialog or capture holds the panel
    [InlineData(false, 0, "CopilotKeyPressFocusFrame", false, false)]
    [InlineData(true, 0, "Chrome_WidgetWin_1", false, false)]
    public void The_Copilot_keys_focus_frame_closes_an_open_panel_at_once(bool visible, int modal, string taker, bool keep, bool closes)
    {
        var asked = false;
        Assert.Equal(closes, PopupBlurPolicy.ClosesOnKeyPress(visible, modal, taker, () => { asked = true; return keep; }));
        Assert.Equal(visible && modal == 0 && taker == "CopilotKeyPressFocusFrame", asked); // asked only when it matters
    }

    [Theory]
    [InlineData(true, 0, true, true)]
    [InlineData(false, 0, true, false)]
    [InlineData(true, 1, true, false)]
    [InlineData(true, 0, false, false)] // no moved spot: nothing to forget
    public void A_pinned_moved_panel_watches_for_a_click_into_another_app(bool pinned, int modal, bool moved, bool watches) =>
        Assert.Equal(watches, PopupBlurPolicy.WatchesClickAway(pinned, modal, moved));

    [Fact]
    public void Only_another_apps_window_counts_as_clicking_away()
    {
        Assert.True(PopupBlurPolicy.IsClickAway(true, true, Other, Panel, "Notepad", () => false));
        Assert.False(PopupBlurPolicy.IsClickAway(false, true, Other, Panel, "Notepad", () => false)); // closed by the key meanwhile
        Assert.False(PopupBlurPolicy.IsClickAway(true, false, Other, Panel, "Notepad", () => false)); // unpinned (forgets anyway)
        Assert.False(PopupBlurPolicy.IsClickAway(true, true, 0, Panel, "", () => false));
        Assert.False(PopupBlurPolicy.IsClickAway(true, true, Panel, Panel, "WinUIDesktopWin32WindowClass", () => false));
        Assert.False(PopupBlurPolicy.IsClickAway(true, true, Other, Panel, "Shell_TrayWnd", () => false));
        Assert.False(PopupBlurPolicy.IsClickAway(true, true, Other, Panel, "Notepad", () => true)); // Hotline's own or a CLI's
    }

    [Theory]
    [InlineData(100, 0, 1, true)]
    [InlineData(100, 2, 1, true)] // the first show after launch: up to three times
    [InlineData(100, 3, 1, false)]
    [InlineData(100, 0, 2, true)]
    [InlineData(100, 1, 2, false)] // later shows: once
    [InlineData(1500, 0, 1, false)] // too long after opening: the user clicked away
    public void Focus_lost_right_after_opening_is_taken_back(long sinceShownMs, int reclaimed, int shows, bool reclaims) =>
        Assert.Equal(reclaims, PopupBlurPolicy.ReclaimsFocus(sinceShownMs, reclaimed, shows));

    [Fact]
    public void A_blur_hides_unless_focus_came_back_is_pending_or_went_to_a_program_hotline_started()
    {
        BlurAction Consider(nint fg, bool retried = false, bool console = false, bool visible = true, bool hideOnBlur = true, int modal = 0, bool pinned = false) =>
            PopupBlurPolicy.Consider(visible, hideOnBlur, modal, pinned, fg, Panel, retried, () => console);
        Assert.Equal(BlurAction.Hide, Consider(Other));
        Assert.Equal(BlurAction.Keep, Consider(Panel)); // it came back
        Assert.Equal(BlurAction.Retry, Consider(0)); // mid-switch: look again
        Assert.Equal(BlurAction.Hide, Consider(0, retried: true));
        Assert.Equal(BlurAction.Reclaim, Consider(Other, console: true));
        Assert.Equal(BlurAction.Keep, Consider(Other, visible: false));
        Assert.Equal(BlurAction.Keep, Consider(Other, hideOnBlur: false));
        Assert.Equal(BlurAction.Keep, Consider(Other, modal: 1));
        Assert.Equal(BlurAction.Keep, Consider(Other, pinned: true));
    }

    [Theory]
    [InlineData("ConsoleWindowClass", true, false, false, true)]
    [InlineData("PseudoConsoleWindow", false, true, false, true)]
    [InlineData("ConsoleWindowClass", false, false, false, false)]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS", true, false, false, false)] // Terminal's window isn't in the job: only time tells
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS", false, true, false, true)]
    [InlineData("ConsoleWindowClass", true, true, true, false)] // once per launch: never a tug of war
    [InlineData("Chrome_WidgetWin_1", true, true, false, false)] // a browser a CLI opens to sign in
    public void Only_a_console_of_a_program_hotline_started_keeps_the_panel(string cls, bool ours, bool recent, bool reclaimed, bool expected) =>
        Assert.Equal(expected, PopupBlurPolicy.IsOurConsole(cls, () => ours, recent, reclaimed));
}
