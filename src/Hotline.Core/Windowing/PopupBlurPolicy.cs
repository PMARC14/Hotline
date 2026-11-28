namespace Hotline.Core.Windowing;

/// <summary>What a focus loss does to the open panel (see <see cref="PopupBlurPolicy.Consider"/>).</summary>
public enum BlurAction
{
    /// <summary>Nothing: focus came back, or the panel stays (pinned, a dialog, hide-on-blur off, already hidden).</summary>
    Keep,
    /// <summary>No window has focus yet (mid-switch): look again after <see cref="PopupBlurPolicy.RetryMs"/>.</summary>
    Retry,
    /// <summary>A console of a program Hotline started took it: take focus back and keep the panel.</summary>
    Reclaim,
    Hide,
}

/// <summary>
/// The panel's focus-loss decisions, moved out of PopupWindow unchanged. Delays are returned as constants; the App runs the
/// timers. Arguments that cost a window lookup are functions, asked only when the decision needs them.
/// </summary>
public static class PopupBlurPolicy
{
    /// <summary>The second look when no window had focus.</summary>
    public const int RetryMs = 150;
    /// <summary>A pinned panel decides this long after losing focus whether the user clicked into another app (the Copilot
    /// key itself takes focus briefly, and then closes the panel, which isn't clicking away).</summary>
    public const int ClickAwayCheckMs = 600;
    /// <summary>Focus lost this soon after opening is Windows handing it back (a cold start), not the user.</summary>
    public const int ReclaimWindowMs = 1500;

    /// <summary>
    /// 26H2: a Copilot key press first gives focus to Explorer's focus frame, then sends the toggle ~0.1 s later. A panel still
    /// showing when the toggle hides it made Explorer play its warning sound, so any open panel closes at once, unless
    /// <paramref name="keep"/> (dictation runs, or the tap does something other than toggling).
    /// </summary>
    public static bool ClosesOnKeyPress(bool visible, int modal, string takerClass, Func<bool> keep) =>
        visible && modal == 0 && ShellSurfaces.IsKeyFocusFrame(takerClass) && !keep();

    /// <summary>A pinned panel with a moved spot forgets it when the user clicks into another app: watch this focus loss.</summary>
    public static bool WatchesClickAway(bool pinned, int modal, bool hasSessionSpot) => pinned && modal == 0 && hasSessionSpot;

    /// <summary>After <see cref="ClickAwayCheckMs"/>: the window with focus is another app's (not the panel, the shell, or a
    /// process that is Hotline's or one it started, which <paramref name="ours"/> says).</summary>
    public static bool IsClickAway(bool visible, bool pinned, nint foreground, nint panel, string foregroundClass, Func<bool> ours) =>
        visible && pinned && foreground != 0 && foreground != panel && !ShellSurfaces.IsShell(foregroundClass) && !ours();

    /// <summary>Take focus back instead of hiding: right after opening, once per show (three times on the first show after launch).</summary>
    public static bool ReclaimsFocus(long sinceShownMs, int reclaimed, int shows) =>
        sinceShownMs < ReclaimWindowMs && reclaimed < (shows <= 1 ? 3 : 1);

    /// <summary>Focus went elsewhere: hide, unless it came back, isn't there yet, or went to a console Hotline started.</summary>
    public static BlurAction Consider(bool visible, bool hideOnBlur, int modal, bool pinned, nint foreground, nint panel, bool retried,
        Func<bool> ourConsole)
    {
        if (!visible || !hideOnBlur || modal != 0 || pinned) return BlurAction.Keep; // may have changed during the retry
        if (foreground == panel) return BlurAction.Keep; // it came back
        if (foreground == 0 && !retried) return BlurAction.Retry;
        if (foreground != 0 && ourConsole()) return BlurAction.Reclaim;
        return BlurAction.Hide;
    }

    /// <summary>
    /// Only console/terminal windows count (never a browser a CLI opens to sign in), at most once per program launch: a plain
    /// console must belong to a process Hotline started (<paramref name="processIsOurs"/>) or appear right after a launch; a
    /// Windows Terminal window (owned by WindowsTerminal.exe, so the job can't tell) must appear right after a launch.
    /// </summary>
    public static bool IsOurConsole(string windowClass, Func<bool> processIsOurs, bool launchedRecently, bool reclaimedThisLaunch)
    {
        if (reclaimedThisLaunch) return false;
        if (windowClass is "ConsoleWindowClass" or "PseudoConsoleWindow") return processIsOurs() || launchedRecently;
        return windowClass == "CASCADIA_HOSTING_WINDOW_CLASS" && launchedRecently;
    }
}
