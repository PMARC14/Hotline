namespace Hotline.Core.Windowing;

/// <summary>
/// Immutable snapshot of a candidate foreground window for placement and capture decisions.
/// </summary>
public readonly record struct WindowCandidate(
    nint Handle,
    bool Exists = true,
    bool IdentityMatches = true,
    bool IsHotline = false,
    bool Visible = true,
    bool Minimized = false,
    bool Cloaked = false,
    bool IsOwnedDialog = false,
    string ClassName = "",
    uint ProcessId = 0,
    uint ThreadId = 0);

/// <summary>
/// Decides whether a candidate window is a real user application window and chooses between the live foreground and a remembered window.
/// </summary>
public static class ForegroundWindowPolicy
{
    public static bool IsReal(WindowCandidate c) =>
        c.Handle != 0 && c.Exists && c.IdentityMatches && !c.IsHotline &&
        c.Visible && !c.Minimized && !c.Cloaked && !c.IsOwnedDialog &&
        !string.IsNullOrEmpty(c.ClassName) && c.ClassName != "<invalid window>" &&
        !ShellSurfaces.IsShell(c.ClassName);

    public static nint Choose(WindowCandidate live, WindowCandidate remembered) =>
        IsReal(live) ? live.Handle : IsReal(remembered) ? remembered.Handle : 0;

    public static WindowCandidate UpdateRemembered(WindowCandidate incoming, WindowCandidate remembered) =>
        IsReal(incoming) ? incoming : remembered;

    /// <summary>
    /// What an "active monitor" open follows after <paramref name="incoming"/> took focus: a real app brings it back to the
    /// remembered window; the desktop or a taskbar (the user moved to another monitor's empty desktop) switches it to the
    /// cursor, since the desktop window spans every monitor; anything else (the Copilot key's focus frame, Start) keeps it.
    /// </summary>
    public static PlaceBy UpdatePlaceBy(WindowCandidate incoming, PlaceBy current)
    {
        if (IsReal(incoming)) return PlaceBy.RememberedWindow;
        var userLocation = incoming.Handle != 0 && incoming.Exists && !incoming.IsHotline && !incoming.IsOwnedDialog &&
            ShellSurfaces.IsUserLocation(incoming.ClassName);
        return userLocation ? PlaceBy.Cursor : current;
    }
}

/// <summary>Where an "active monitor" open goes: the remembered app window's monitor, or the cursor's.</summary>
public enum PlaceBy { RememberedWindow, Cursor }
