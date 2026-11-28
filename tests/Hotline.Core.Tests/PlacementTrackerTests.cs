using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>
/// The foreground tracking that decides where an "active monitor" open goes and where focus goes back on close, as
/// ForegroundWindowTracker did it before the move to Core (pinned here, behaviour unchanged).
/// </summary>
public sealed class PlacementTrackerTests
{
    private static readonly WindowCandidate App = new(0x100, ClassName: "Chrome_WidgetWin_1", ProcessId: 10, ThreadId: 11);
    private static readonly WindowCandidate Other = new(0x200, ClassName: "Notepad", ProcessId: 20, ThreadId: 21);
    private static readonly WindowCandidate Desktop = new(0x300, ClassName: "WorkerW", ProcessId: 30, ThreadId: 31);
    private static readonly WindowCandidate Taskbar = new(0x301, ClassName: "Shell_SecondaryTrayWnd", ProcessId: 30, ThreadId: 32);
    private static readonly WindowCandidate KeyFrame = new(0x400, ClassName: "CopilotKeyPressFocusFrame", ProcessId: 30, ThreadId: 33);
    private static readonly WindowCandidate Start = new(0x500, ClassName: "Windows.UI.Core.CoreWindow", ProcessId: 40, ThreadId: 41);

    /// <summary>The windows that exist now; a snapshot with an expected identity checks it like the App's Snapshot does.</summary>
    private readonly Dictionary<nint, WindowCandidate> _windows = new[] { App, Other, Desktop, Taskbar, KeyFrame, Start }.ToDictionary(w => w.Handle);

    private WindowCandidate Snapshot(nint hwnd, WindowCandidate? expected)
    {
        if (hwnd == 0) return default;
        if (!_windows.TryGetValue(hwnd, out var w)) return new WindowCandidate(hwnd, Exists: false);
        return expected is { } e && (e.ProcessId != w.ProcessId || e.ThreadId != w.ThreadId) ? w with { IdentityMatches = false } : w;
    }

    private PlacementTracker Tracker() => new(Snapshot);

    [Fact]
    public void A_real_app_is_remembered_and_placement_follows_it()
    {
        var t = Tracker();
        Assert.False(t.NoteForeground(App.Handle));
        Assert.Equal(PlaceBy.RememberedWindow, t.PlaceBy);
        Assert.Equal(App.Handle, t.ResolveForShow(KeyFrame.Handle));
    }

    [Theory]
    [InlineData(0x300)] // the desktop
    [InlineData(0x301)] // another monitor's taskbar
    public void The_desktop_or_a_taskbar_switches_placement_to_the_cursor_and_is_the_place_to_give_focus_back(int shell)
    {
        var t = Tracker();
        t.NoteForeground(App.Handle);
        Assert.True(t.NoteForeground(shell)); // changed: the App logs it
        Assert.Equal(PlaceBy.Cursor, t.PlaceBy);
        Assert.Equal(shell, t.Location);
        Assert.Equal(App.Handle, t.ResolveForShow(KeyFrame.Handle)); // still the remembered app for capture
        Assert.True(t.NoteForeground(Other.Handle));
        Assert.Equal(PlaceBy.RememberedWindow, t.PlaceBy);
        Assert.Equal(0, t.Location);
    }

    [Fact]
    public void Focus_the_key_frame_hands_on_is_not_the_user_moving()
    {
        var t = Tracker();
        t.NoteForeground(App.Handle);
        t.NoteForeground(KeyFrame.Handle);
        Assert.False(t.NoteForeground(Desktop.Handle));
        Assert.Equal(PlaceBy.RememberedWindow, t.PlaceBy);
        Assert.Equal(0, t.Location);
    }

    [Fact]
    public void After_the_panel_had_focus_a_desktop_click_counts_again()
    {
        var t = Tracker();
        t.NoteForeground(App.Handle);
        t.NoteForeground(KeyFrame.Handle);
        t.NotePanelFocused(); // the panel's own focus isn't hooked: the frame's hand-off is over
        Assert.True(t.NoteForeground(Desktop.Handle));
        Assert.Equal(Desktop.Handle, t.Location);
    }

    [Fact]
    public void Start_or_search_keeps_the_placement_source()
    {
        var t = Tracker();
        t.NoteForeground(Desktop.Handle);
        Assert.False(t.NoteForeground(Start.Handle));
        Assert.Equal(PlaceBy.Cursor, t.PlaceBy);
        Assert.Equal(Desktop.Handle, t.Location);
    }

    [Fact]
    public void A_location_that_closed_is_no_longer_the_place_to_give_focus_back()
    {
        var t = Tracker();
        t.NoteForeground(Desktop.Handle);
        _windows.Remove(Desktop.Handle);
        Assert.Equal(0, t.Location);
    }

    [Fact]
    public void Show_prefers_a_real_live_window_then_the_remembered_one_then_nothing()
    {
        var t = Tracker();
        t.NoteForeground(App.Handle);
        Assert.Equal(Other.Handle, t.ResolveForShow(Other.Handle));
        Assert.Equal(Other.Handle, t.ResolveForShow(KeyFrame.Handle)); // the live winner became the remembered one
        _windows.Remove(Other.Handle);
        Assert.Equal(0, t.ResolveForShow(KeyFrame.Handle));
        Assert.Equal(0, t.ShowTarget.Handle);
        Assert.Equal(0, t.Remembered.Handle);
    }

    [Fact]
    public void A_reused_handle_with_another_identity_is_not_the_remembered_window()
    {
        var t = Tracker();
        t.NoteForeground(App.Handle);
        _windows[App.Handle] = App with { ProcessId = 99 };
        Assert.Equal(0, t.ResolveForShow(KeyFrame.Handle));
        Assert.Equal(0, t.Remembered.Handle);
    }

    [Fact]
    public void A_show_target_whose_handle_was_reused_is_dropped()
    {
        var t = Tracker();
        t.NoteForeground(App.Handle);
        Assert.Equal(App.Handle, t.ResolveForShow(KeyFrame.Handle));
        _windows[App.Handle] = App with { ThreadId = 99 };
        Assert.Equal(0, t.RevalidateShowTarget(App.Handle));
        Assert.Equal(0, t.ShowTarget.Handle);
    }

    [Fact]
    public void Revalidating_keeps_a_live_target_and_drops_one_that_went_away()
    {
        var t = Tracker();
        t.NoteForeground(App.Handle);
        Assert.Equal(App.Handle, t.ResolveForShow(KeyFrame.Handle));
        Assert.Equal(App.Handle, t.RevalidateShowTarget(App.Handle));
        Assert.Equal(App.Handle, t.ShowTarget.Handle);
        _windows[App.Handle] = App with { Minimized = true };
        Assert.Equal(0, t.RevalidateShowTarget(App.Handle));
        Assert.Equal(0, t.ShowTarget.Handle);
        Assert.Equal(0, t.Remembered.Handle);
        Assert.Equal(0, t.RevalidateShowTarget(0));
    }

    [Fact]
    public void Tracked_windows_are_the_show_target_and_the_remembered_one()
    {
        var t = Tracker();
        t.NoteForeground(App.Handle);
        Assert.True(t.IsTracked(App.Handle));
        Assert.False(t.IsTracked(Other.Handle));
        Assert.False(t.IsTracked(0));
    }
}
