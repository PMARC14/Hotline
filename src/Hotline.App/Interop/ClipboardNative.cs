using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Interop.UIAutomationClient;

namespace Hotline.App.Interop;

internal static class ClipboardNative
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, nint hWnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern nint DispatchMessage(ref MSG msg);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam, LParam;
        public uint Time;
        public int PtX, PtY;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] public static extern bool CloseClipboard();
    [DllImport("user32.dll")] public static extern bool EmptyClipboard();
    [DllImport("user32.dll")] public static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] public static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] public static extern nint SetClipboardData(uint format, nint mem);
    [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll")] public static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] public static extern nint GlobalFree(nint mem);
    [DllImport("kernel32.dll")] public static extern nint GlobalLock(nint mem);
    [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(nint mem);
    [DllImport("kernel32.dll")] public static extern nuint GlobalSize(nint mem);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public InputUnion U;
    }

    // The union must be as large as its biggest member (MOUSEINPUT) for SendInput to accept the size.
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }

    public static INPUT Key(ushort vk, bool up) =>
        new() { Type = 1 /* INPUT_KEYBOARD */, U = new InputUnion { Keyboard = new KEYBDINPUT { Vk = vk, Flags = up ? 0x0002u /* KEYEVENTF_KEYUP */ : 0 } } };
}
