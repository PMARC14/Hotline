using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class ForegroundWindowPolicyTests
{
    private static readonly WindowCandidate RememberedApp = new(
        Handle: 0x1001,
        ClassName: "CASCADIA_HOSTING_WINDOW_CLASS",
        ProcessId: 4200,
        ThreadId: 4201);

    private static readonly WindowCandidate LiveApp = new(
        Handle: 0x2002,
        ClassName: "Chrome_WidgetWin_1",
        ProcessId: 5200,
        ThreadId: 5201);

    [Fact]
    public void Choose_uses_remembered_app_after_copilot_frame()
    {
        var copilotFrame = new WindowCandidate(
            Handle: 0x3003,
            ClassName: "CopilotKeyPressFocusFrame",
            ProcessId: 1100,
            ThreadId: 1101);

        var chosen = ForegroundWindowPolicy.Choose(copilotFrame, RememberedApp);

        Assert.Equal(RememberedApp.Handle, chosen);
    }

    public static TheoryData<string, WindowCandidate> InvalidLiveCandidates => new()
    {
        { "zero", new WindowCandidate(Handle: 0) },
        { "shell:Shell_TrayWnd", LiveApp with { ClassName = "Shell_TrayWnd" } },
        { "shell:Shell_SecondaryTrayWnd", LiveApp with { ClassName = "Shell_SecondaryTrayWnd" } },
        { "shell:Progman", LiveApp with { ClassName = "Progman" } },
        { "shell:WorkerW", LiveApp with { ClassName = "WorkerW" } },
        { "shell:Windows.UI.Core.CoreWindow", LiveApp with { ClassName = "Windows.UI.Core.CoreWindow" } },
        { "shell:XamlExplorerHostIslandWindow", LiveApp with { ClassName = "XamlExplorerHostIslandWindow" } },
        { "shell:CopilotKeyPressFocusFrame", LiveApp with { ClassName = "CopilotKeyPressFocusFrame" } },
        { "own-settings", LiveApp with { IsHotline = true, ClassName = "WinUIDesktopWin32WindowClass" } },
        { "file-picker", LiveApp with { IsOwnedDialog = true, ClassName = "#32770" } },
        { "cloaked", LiveApp with { Cloaked = true } },
        { "minimized", LiveApp with { Minimized = true } },
        { "hidden", LiveApp with { Visible = false } },
        { "destroyed", LiveApp with { Exists = false } },
        { "reused-identity", LiveApp with { IdentityMatches = false } },
    };

    [Theory]
    [MemberData(nameof(InvalidLiveCandidates))]
    public void Choose_falls_back_to_remembered_when_live_is_ineligible(string reason, WindowCandidate invalidLive)
    {
        Assert.False(ForegroundWindowPolicy.IsReal(invalidLive), $"Expected {reason} to be rejected by IsReal.");
        Assert.Equal(RememberedApp.Handle, ForegroundWindowPolicy.Choose(invalidLive, RememberedApp));
        Assert.Equal(RememberedApp, ForegroundWindowPolicy.UpdateRemembered(invalidLive, RememberedApp));
    }

    [Fact]
    public void Choose_prefers_live_real_app_over_remembered_app()
    {
        Assert.True(ForegroundWindowPolicy.IsReal(LiveApp));
        Assert.Equal(LiveApp.Handle, ForegroundWindowPolicy.Choose(LiveApp, RememberedApp));
        Assert.Equal(LiveApp, ForegroundWindowPolicy.UpdateRemembered(LiveApp, RememberedApp));
    }

    [Theory]
    [MemberData(nameof(InvalidLiveCandidates))]
    public void Choose_returns_zero_when_live_and_remembered_are_both_invalid(string reason, WindowCandidate invalidRemembered)
    {
        var copilotFrame = LiveApp with { ClassName = "CopilotKeyPressFocusFrame" };
        Assert.Equal((nint)0, ForegroundWindowPolicy.Choose(copilotFrame, invalidRemembered));
        Assert.True(reason.Length > 0);
    }

    [Fact]
    public void Choose_rejects_remembered_window_whose_identity_was_reused_or_went_stale()
    {
        var copilotFrame = LiveApp with { ClassName = "CopilotKeyPressFocusFrame" };
        var reusedRemembered = RememberedApp with { IdentityMatches = false };
        var closedRemembered = RememberedApp with { Exists = false };

        Assert.Equal((nint)0, ForegroundWindowPolicy.Choose(copilotFrame, reusedRemembered));
        Assert.Equal((nint)0, ForegroundWindowPolicy.Choose(copilotFrame, closedRemembered));
    }

    // The desktop and the taskbars say where the user is (a click on another monitor's empty desktop), even though
    // they are never capture targets. Transient shell surfaces (the Copilot key's focus frame, Start) say nothing.
    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    public void Placement_follows_the_cursor_after_the_user_moves_to_the_desktop_or_a_taskbar(string cls)
    {
        var desktop = LiveApp with { ClassName = cls };
        Assert.Equal(PlaceBy.Cursor, ForegroundWindowPolicy.UpdatePlaceBy(desktop, PlaceBy.RememberedWindow));
        Assert.Equal(RememberedApp, ForegroundWindowPolicy.UpdateRemembered(desktop, RememberedApp)); // capture target unchanged
    }

    [Theory]
    [InlineData("CopilotKeyPressFocusFrame")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    [InlineData("XamlExplorerHostIslandWindow")]
    public void Transient_shell_surfaces_keep_the_placement_source(string cls)
    {
        var transient = LiveApp with { ClassName = cls };
        Assert.Equal(PlaceBy.Cursor, ForegroundWindowPolicy.UpdatePlaceBy(transient, PlaceBy.Cursor));
        Assert.Equal(PlaceBy.RememberedWindow, ForegroundWindowPolicy.UpdatePlaceBy(transient, PlaceBy.RememberedWindow));
    }

    [Fact]
    public void A_real_app_window_brings_placement_back_to_the_remembered_window()
        => Assert.Equal(PlaceBy.RememberedWindow, ForegroundWindowPolicy.UpdatePlaceBy(LiveApp, PlaceBy.Cursor));

    [Theory]
    [InlineData(true, false, false)]  // Hotline's own window
    [InlineData(false, true, false)]  // a dialog Hotline owns
    [InlineData(false, false, true)]  // gone
    public void Hotline_windows_and_dead_handles_keep_the_placement_source(bool isHotline, bool ownedDialog, bool gone)
    {
        var c = LiveApp with { ClassName = "Progman", IsHotline = isHotline, IsOwnedDialog = ownedDialog, Exists = !gone };
        Assert.Equal(PlaceBy.RememberedWindow, ForegroundWindowPolicy.UpdatePlaceBy(c, PlaceBy.RememberedWindow));
    }

    // 26H2: when the Copilot key closes the panel, Explorer's focus frame holds focus and, once the key is released, hands
    // it to some window (often an app on the other monitor). That is Windows' choice, not the user moving.
    [Fact]
    public void Focus_handed_on_by_the_copilot_key_frame_keeps_the_placement_source()
    {
        Assert.Equal(PlaceBy.Cursor, ForegroundWindowPolicy.UpdatePlaceBy(LiveApp, PlaceBy.Cursor, previousClass: "CopilotKeyPressFocusFrame"));
        Assert.Equal(PlaceBy.RememberedWindow, ForegroundWindowPolicy.UpdatePlaceBy(LiveApp, PlaceBy.Cursor, previousClass: "Progman"));
        Assert.Equal(PlaceBy.RememberedWindow, ForegroundWindowPolicy.UpdatePlaceBy(LiveApp, PlaceBy.Cursor, previousClass: ""));
    }

    [Theory]
    [InlineData(true, "", true)]                                // closed with the key or Esc while the panel has focus
    [InlineData(false, "CopilotKeyPressFocusFrame", true)]     // 26H2: the key's frame holds focus
    [InlineData(false, "Windows.UI.Core.CoreWindow", false)]   // the user opened Start or Search: leave it
    [InlineData(false, "XamlExplorerHostIslandWindow", false)] // a shell flyout
    [InlineData(false, "Shell_TrayWnd", false)]                // the user clicked the taskbar
    [InlineData(false, "Chrome_WidgetWin_1", false)]           // the user clicked an app
    public void Focus_goes_back_on_close_only_from_the_panel_or_the_key_frame(bool panelHasFocus, string cls, bool expected)
        => Assert.Equal(expected, ForegroundWindowPolicy.GivesFocusBackOnClose(panelHasFocus, cls));

    [Fact]
    public void A_desktop_or_taskbar_handed_focus_by_the_key_frame_is_not_where_the_user_went()
    {
        var taskbar = LiveApp with { ClassName = "Shell_SecondaryTrayWnd" };
        Assert.True(ForegroundWindowPolicy.IsUserLocation(taskbar));
        Assert.False(ForegroundWindowPolicy.IsUserLocation(taskbar, previousClass: "CopilotKeyPressFocusFrame"));
        Assert.False(ForegroundWindowPolicy.IsUserLocation(taskbar with { Exists = false }));
    }
}
