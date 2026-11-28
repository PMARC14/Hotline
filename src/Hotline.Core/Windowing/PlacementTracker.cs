namespace Hotline.Core.Windowing;

/// <summary>
/// Where the user is, from the windows that took focus: the last real app window (placement and capture target), the window
/// chosen when the panel opened, what an "active monitor" open follows (<see cref="PlaceBy"/>), and the desktop or taskbar the
/// user moved to (focus goes back there on close). Fed by the App's foreground hook; every window is read through the
/// snapshot function, so the rules (<see cref="ForegroundWindowPolicy"/>) and their order are tested here.
/// </summary>
/// <param name="snapshot">A window as it is now; with an expected window, <see cref="WindowCandidate.IdentityMatches"/> says
/// whether it is still that window (a reused handle is not).</param>
public sealed class PlacementTracker(Func<nint, WindowCandidate?, WindowCandidate> snapshot)
{
    private nint _location;

    /// <summary>The last real app window that took focus.</summary>
    public WindowCandidate Remembered { get; private set; }
    /// <summary>The window the panel opened over (chosen by <see cref="ResolveForShow"/>, kept current by <see cref="RevalidateShowTarget"/>).</summary>
    public WindowCandidate ShowTarget { get; private set; }
    /// <summary>What an "active monitor" open follows: the remembered app, or the cursor after a click on the desktop or a taskbar.</summary>
    public PlaceBy PlaceBy { get; private set; }
    /// <summary>The class of the last window that took focus (any kind); empty after the panel took focus.</summary>
    public string LastClass { get; private set; } = "";

    /// <summary>The desktop or taskbar the user moved to (placement follows the cursor), while it still exists; otherwise 0.</summary>
    public nint Location => PlaceBy == PlaceBy.Cursor && _location != 0 && snapshot(_location, null).Exists ? _location : 0;

    /// <summary>A window took focus. True when <see cref="PlaceBy"/> changed (the App logs it).</summary>
    public bool NoteForeground(nint hwnd)
    {
        var candidate = snapshot(hwnd, null);
        Remembered = ForegroundWindowPolicy.UpdateRemembered(candidate, Remembered);
        var placeBy = ForegroundWindowPolicy.UpdatePlaceBy(candidate, PlaceBy, LastClass);
        if (ForegroundWindowPolicy.IsUserLocation(candidate, LastClass)) _location = hwnd;
        LastClass = candidate.ClassName;
        var changed = placeBy != PlaceBy;
        PlaceBy = placeBy;
        return changed;
    }

    /// <summary>The panel took focus (its own events aren't hooked): what follows is no longer the key frame's hand-off.</summary>
    public void NotePanelFocused() => LastClass = "";

    /// <summary>The window the panel opens over: a real live foreground window, else the remembered one (revalidated), else 0.</summary>
    public nint ResolveForShow(nint liveForeground)
    {
        var live = snapshot(liveForeground, null);
        var remembered = Remembered.Handle != 0 ? snapshot(Remembered.Handle, Remembered) : default;
        if (!ForegroundWindowPolicy.IsReal(remembered)) Remembered = default;
        var chosen = ForegroundWindowPolicy.Choose(live, remembered);
        if (chosen != 0)
        {
            var winner = chosen == live.Handle && ForegroundWindowPolicy.IsReal(live) ? live : remembered;
            Remembered = winner;
            ShowTarget = winner;
        }
        else ShowTarget = default;
        return chosen;
    }

    /// <summary>The window again if it is still the same real window (identity, visible, not minimized or cloaked), else 0 (and forgotten).</summary>
    public nint RevalidateShowTarget(nint hwnd)
    {
        if (hwnd == 0)
        {
            ShowTarget = default;
            return 0;
        }
        var expected = ShowTarget.Handle == hwnd ? ShowTarget : Remembered.Handle == hwnd ? Remembered : (WindowCandidate?)null;
        var candidate = snapshot(hwnd, expected);
        if (ForegroundWindowPolicy.IsReal(candidate))
        {
            ShowTarget = candidate;
            return hwnd;
        }
        if (ShowTarget.Handle == hwnd) ShowTarget = default;
        if (Remembered.Handle == hwnd) Remembered = default;
        return 0;
    }

    /// <summary>The show target or the remembered window (never 0).</summary>
    public bool IsTracked(nint hwnd) => hwnd != 0 && (hwnd == ShowTarget.Handle || hwnd == Remembered.Handle);
}
