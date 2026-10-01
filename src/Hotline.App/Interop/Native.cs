using System.Runtime.InteropServices;

namespace Hotline.App.Interop;

/// <summary>All Win32 interop used by Hotline, hand-written to avoid generator dependencies.</summary>
internal static class Native
{
    public const uint WM_COMMAND = 0x0111, WM_HOTKEY = 0x0312, WM_NULL = 0x0000;
    public const uint WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B;
    public const uint WM_APP = 0x8000;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;
    public const int NIM_ADD = 0, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const int NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4;
    public const int NOTIFYICON_VERSION_4 = 4;
    public const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;
    public const uint TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20;
    public const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    public const ushort VT_UINT = 23;

    public delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData);

    [DllImport("comctl32.dll")] public static extern bool SetWindowSubclass(nint hWnd, SubclassProc proc, nuint id, nuint refData);
    [DllImport("comctl32.dll")] public static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint hWnd, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(nint hMonitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] public static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenu(nint hMenu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] public static extern bool TrackPopupMenuEx(nint hMenu, uint flags, int x, int y, nint hWnd, nint tpm);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(nint hMenu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hWnd, System.Text.StringBuilder name, int max);
    [DllImport("shell32.dll")] public static extern int SHGetPropertyStoreForWindow(nint hWnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    public static string ClassNameOf(nint hWnd)
    {
        var sb = new System.Text.StringBuilder(256);
        return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : "<invalid window>";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public int uID;
        public int uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    /// <summary>Only the VT_UINT case is used; size matches x64 PROPVARIANT (24 bytes).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public uint uintVal;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }
}
