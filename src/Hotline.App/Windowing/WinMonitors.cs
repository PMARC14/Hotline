using Hotline.App.Interop;
using Hotline.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Hotline.App.Windowing;

/// <summary>The connected monitors through DisplayArea (work areas) and the Win32 monitor calls (handles, effective DPI, device names).</summary>
internal sealed class WinMonitors : IMonitors
{
    private static double Scale(nint monitor) =>
        Native.GetDpiForMonitor(monitor, Native.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;

    private static RectI Work(DisplayArea area) => new(area.WorkArea.X, area.WorkArea.Y, area.WorkArea.Width, area.WorkArea.Height);

    private static MonitorInfo Info(DisplayArea area, nint monitor) => new(monitor, Work(area), Scale(monitor), Native.MonitorDevice(monitor));

    public MonitorInfo Primary
    {
        get
        {
            var primary = DisplayArea.Primary;
            return Info(primary, Win32Interop.GetMonitorFromDisplayId(primary.DisplayId));
        }
    }

    public IEnumerable<MonitorInfo> All => DisplayArea.FindAll().Select(a => Info(a, Win32Interop.GetMonitorFromDisplayId(a.DisplayId)));

    public MonitorInfo? OfWindow(nint window)
    {
        var area = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(window), DisplayAreaFallback.Nearest);
        var monitor = Native.MonitorFromWindow(window, Native.MONITOR_DEFAULTTONEAREST);
        return area is null || monitor == 0 ? null : Info(area, monitor);
    }

    public MonitorInfo AtCursor()
    {
        Native.GetCursorPos(out var pt);
        var area = DisplayArea.GetFromPoint(new PointInt32(pt.X, pt.Y), DisplayAreaFallback.Nearest) ?? DisplayArea.Primary;
        return Info(area, Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST));
    }

    public MonitorInfo? AtRect(RectI r, bool nearest)
    {
        var rect = new Native.RECT { Left = r.X, Top = r.Y, Right = r.X + r.Width, Bottom = r.Y + r.Height };
        var monitor = Native.MonitorFromRect(ref rect, nearest ? Native.MONITOR_DEFAULTTONEAREST : Native.MONITOR_DEFAULTTONULL);
        if (monitor == 0) return null;
        var area = DisplayArea.GetFromDisplayId(Win32Interop.GetDisplayIdFromMonitor(monitor));
        return area is null ? null : Info(area, monitor);
    }

    public MonitorInfo? Valid(nint monitor) => PopupWindow.ValidMonitor(monitor) is { } m ? Info(m.Area, m.Monitor) : null;
}
