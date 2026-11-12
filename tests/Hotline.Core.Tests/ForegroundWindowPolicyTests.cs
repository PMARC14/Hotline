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
}
