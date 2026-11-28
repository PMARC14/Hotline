namespace Hotline.Core.Windowing;

/// <summary>A connected monitor: its handle, work area (physical pixels), effective scale and device name.</summary>
public readonly record struct MonitorInfo(nint Handle, RectI Work, double Scale, string? Device);

/// <summary>The panel window as <see cref="PanelGeometry"/> sees it (physical pixels). The App implements it on the WinUI window.</summary>
public interface IPanelWindow
{
    bool IsVisible { get; }
    /// <summary>The window's rectangle now.</summary>
    RectI Rect { get; }
    /// <summary>Window height minus client height: the frame the content must fit inside.</summary>
    int FrameHeight { get; }
    /// <summary>The panel's own top plus bottom padding (DIPs).</summary>
    double PaddingDip { get; }
    /// <summary>The monitor the window is on (nearest).</summary>
    nint Monitor { get; }
    /// <summary>The work area of the display the window is on (nearest), null when there is none.</summary>
    RectI? DisplayWork { get; }
    /// <summary>The scale WinUI draws the content at, null before the first layout.</summary>
    double? RasterizationScale { get; }
    void MoveAndResize(RectI rect);
    void Move(int x, int y);
    /// <summary>Starts calling <see cref="PanelGeometry.OnFrame"/> once per display frame, with the seconds since the last call.</summary>
    void StartFrames();
    void StopFrames();
}

/// <summary>The connected monitors. The App implements it with DisplayArea and the Win32 monitor calls.</summary>
public interface IMonitors
{
    MonitorInfo Primary { get; }
    IEnumerable<MonitorInfo> All { get; }
    /// <summary>The monitor a window is on (nearest), null for a window that has none (it closed).</summary>
    MonitorInfo? OfWindow(nint window);
    /// <summary>The monitor at the cursor (nearest; the primary display's work area when none).</summary>
    MonitorInfo AtCursor();
    /// <summary>The monitor a rectangle is on: the nearest one, or with <paramref name="nearest"/> false, null when it is on none.</summary>
    MonitorInfo? AtRect(RectI rect, bool nearest);
    /// <summary>The monitor still connected, else null (unplugged).</summary>
    MonitorInfo? Valid(nint monitor);
}

/// <summary>Where the user is when the panel is placed (the App's foreground tracking).</summary>
public interface IPlacementSource
{
    /// <summary>What an "active monitor" open follows.</summary>
    PlaceBy PlaceBy { get; }
    /// <summary>The window the panel opened over, revalidated now (and kept), or 0.</summary>
    nint RevalidatePrevious();
    /// <summary>The monitor the user was on when the panel opened after a desktop or taskbar click, else 0.</summary>
    nint UserMonitor { get; }
    /// <summary>A window's class name, for the log.</summary>
    string ClassName(nint window);
}
