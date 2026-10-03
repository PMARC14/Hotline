using System.Runtime.InteropServices;
using Hotline.App.Interop;
using Hotline.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace Hotline.App.Capture;

/// <summary>
/// Copies what is on screen (after the popup has hidden itself) via GDI. Captures the visible pixels of a
/// window's rectangle, so overlapping windows appear too, like a snipping tool. Physical pixels (PerMonitorV2).
/// </summary>
internal static class ScreenCapture
{
    public static bool TryGetWindowRect(nint hwnd, out RectI rect)
    {
        rect = default;
        if (hwnd == 0 || !Native.IsWindow(hwnd) || Native.IsIconic(hwnd)) return false;
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<Native.RECT>()) != 0) return false;
        rect = new RectI(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return rect.Width > 0 && rect.Height > 0;
    }

    public static RectI MonitorRect(nint hwnd)
    {
        var area = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd), DisplayAreaFallback.Primary) ?? DisplayArea.Primary;
        var b = area.OuterBounds;
        return new RectI(b.X, b.Y, b.Width, b.Height);
    }

    /// <summary>The monitor under the mouse pointer (region capture happens where you're looking).</summary>
    public static RectI CursorMonitorRect()
    {
        Native.GetCursorPos(out var pt);
        var area = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(pt.X, pt.Y), DisplayAreaFallback.Primary) ?? DisplayArea.Primary;
        var b = area.OuterBounds;
        return new RectI(b.X, b.Y, b.Width, b.Height);
    }

    /// <summary>Top-down BGRA pixels of the given screen rectangle.</summary>
    public static byte[] GrabBgra(RectI r)
    {
        var screen = Native.GetDC(0);
        var mem = Native.CreateCompatibleDC(screen);
        var bmp = Native.CreateCompatibleBitmap(screen, r.Width, r.Height);
        var old = Native.SelectObject(mem, bmp);
        try
        {
            if (!Native.BitBlt(mem, 0, 0, r.Width, r.Height, screen, r.X, r.Y, Native.SRCCOPY | Native.CAPTUREBLT))
                throw new InvalidOperationException($"BitBlt failed ({Marshal.GetLastWin32Error()})");
            var header = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(), biWidth = r.Width, biHeight = -r.Height, // negative = top-down
                biPlanes = 1, biBitCount = 32, biCompression = 0,
            };
            var pixels = new byte[r.Width * r.Height * 4];
            Native.SelectObject(mem, old); // a bitmap must not be selected into a DC for GetDIBits
            if (Native.GetDIBits(mem, bmp, 0, (uint)r.Height, pixels, ref header, 0) == 0)
                throw new InvalidOperationException("GetDIBits failed");
            return pixels;
        }
        finally
        {
            Native.SelectObject(mem, old);
            Native.DeleteObject(bmp);
            Native.DeleteDC(mem);
            Native.ReleaseDC(0, screen);
        }
    }
}
