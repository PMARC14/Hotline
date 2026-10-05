using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Interop.UIAutomationClient;

namespace Hotline.App.Interop;

/// <summary>
/// Reads the text selected in the app the key was pressed in, before the panel takes focus. UI Automation first
/// (focused element → TextPattern.GetSelection; no keystrokes, clipboard untouched); with
/// <see cref="AttachSelectionMode.Clipboard"/> also Ctrl+C with the clipboard put back exactly. Password fields are
/// never read. The caller's wait is bounded, so opening the panel never waits long.
/// </summary>
internal static class SelectionReader
{
    private const int UIA_TextPatternId = 10014;
    private const int UIA_IsPasswordPropertyId = 30019;
    private static readonly TimeSpan UiaBudget = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan ClipboardBudget = TimeSpan.FromMilliseconds(450);

    public sealed record Result(string Text, string? App, string Via);

    /// <summary>Blocks at most ~150 ms (~600 ms with Ctrl+C). Null when nothing is selected or it can't be read.</summary>
    public static Result? Read(nint foreground, AttachSelectionMode mode, FileLog log)
    {
        if (mode == AttachSelectionMode.Off || foreground == 0) return null;
        Native.GetWindowThreadProcessId(foreground, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return null;
        var sw = Stopwatch.StartNew();
        string? text = null, via = null;
        // UIA calls go into the other process: run them off the UI thread (thread pool = MTA) and stop waiting in time.
        var uia = Task.Run(() => ReadUia(pid));
        try
        {
            if (uia.Wait(UiaBudget)) (text, via) = (uia.Result, "UI Automation");
            else log.Info("selection: UI Automation didn't answer in time");
        }
        catch (AggregateException ex) { log.Debug($"selection: UI Automation failed: {ex.InnerException?.Message}"); }

        if (string.IsNullOrWhiteSpace(text) && mode == AttachSelectionMode.Clipboard)
        {
            var copy = Task.Run(() => ClipboardCopy.CopySelection(ClipboardBudget, log));
            try
            {
                if (copy.Wait(ClipboardBudget + TimeSpan.FromMilliseconds(150))) (text, via) = (copy.Result, "Ctrl+C");
                else log.Error("selection: Ctrl+C copy didn't finish in time");
            }
            catch (AggregateException ex) { log.Error("selection: Ctrl+C copy failed", ex.InnerException); }
        }
        if (string.IsNullOrWhiteSpace(text)) return null;
        log.Info($"selection: {text.Length} chars via {via} in {sw.ElapsedMilliseconds} ms");
        return new Result(text, AppName(foreground, pid), via!);
    }

    private static string? ReadUia(uint pid)
    {
        var automation = new CUIAutomation8();
        var element = automation.GetFocusedElement();
        if (element is null || element.CurrentProcessId != (int)pid) return null; // focus already moved on
        if (element.GetCurrentPropertyValue(UIA_IsPasswordPropertyId) is true) return null;
        if (element.GetCurrentPattern(UIA_TextPatternId) is not IUIAutomationTextPattern pattern) return null;
        var ranges = pattern.GetSelection();
        if (ranges is null) return null;
        var sb = new StringBuilder();
        for (var i = 0; i < ranges.Length && sb.Length <= SelectionAttachment.MaxChars; i++)
        {
            var piece = ranges.GetElement(i).GetText(SelectionAttachment.MaxChars + 1);
            if (string.IsNullOrEmpty(piece)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(piece);
        }
        return sb.ToString();
    }

    /// <summary>"Notepad", "Microsoft Edge", …: the program's description, else its process name, else the window title.</summary>
    private static string? AppName(nint window, uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            if (string.Equals(p.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) return Native.WindowTitle(window);
            try
            {
                if (p.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } description) return description;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { } // elevated or exiting
            return p.ProcessName;
        }
        catch (ArgumentException) { return null; }
    }
}

/// <summary>
/// Ctrl+C into the foreground app with the clipboard restored exactly afterwards (every memory-based format; GDI
/// handle formats are re-synthesized by Windows from their DIB/text equivalents).
/// </summary>
internal static class ClipboardCopy
{
    private const long MaxSnapshotBytes = 64L * 1024 * 1024;

    public static string? CopySelection(TimeSpan budget, FileLog log)
    {
        var deadline = Environment.TickCount64 + (long)budget.TotalMilliseconds;
        // The key's own modifiers (Win/Shift for the Copilot key) must be up, or Ctrl+C becomes another shortcut.
        while (ModifiersDown())
        {
            if (Environment.TickCount64 > deadline) { log.Info("selection: modifiers still held; skipped Ctrl+C"); return null; }
            Thread.Sleep(10);
        }
        var saved = Snapshot(log);
        if (saved is null) return null;
        var before = ClipboardNative.GetClipboardSequenceNumber();
        SendCtrlC();
        while (ClipboardNative.GetClipboardSequenceNumber() == before && Environment.TickCount64 < deadline) Thread.Sleep(10);
        if (ClipboardNative.GetClipboardSequenceNumber() == before) return null; // nothing was copied: clipboard untouched
        Thread.Sleep(20); // let the app finish writing all of its formats
        var text = ReadText();
        Restore(saved, log);
        return text;
    }

    private static bool ModifiersDown() =>
        new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(vk => (ClipboardNative.GetAsyncKeyState(vk) & 0x8000) != 0); // Shift, Ctrl, Alt, LWin, RWin

    private static bool IsHandleFormat(uint f) =>
        f is 2 or 3 or 9 or 14 or 0x80 or 0x82 or 0x83 or 0x8E || f is >= 0x200 and <= 0x3FF; // bitmap, metafile, palette, owner/private/GDI

    private static List<(uint Format, byte[] Data)>? Snapshot(FileLog log)
    {
        if (!OpenWithRetry()) { log.Info("selection: clipboard busy; skipped Ctrl+C"); return null; }
        try
        {
            var formats = new List<(uint, byte[])>();
            long total = 0;
            for (uint f = ClipboardNative.EnumClipboardFormats(0); f != 0; f = ClipboardNative.EnumClipboardFormats(f))
            {
                if (IsHandleFormat(f)) continue;
                var h = ClipboardNative.GetClipboardData(f);
                if (h == 0) continue;
                var size = (long)ClipboardNative.GlobalSize(h);
                if ((total += size) > MaxSnapshotBytes) { log.Info("selection: clipboard too large to restore; skipped Ctrl+C"); return null; }
                var p = ClipboardNative.GlobalLock(h);
                if (p == 0) continue;
                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(p, bytes, 0, (int)size);
                    formats.Add((f, bytes));
                }
                finally { ClipboardNative.GlobalUnlock(h); }
            }
            return formats;
        }
        finally { ClipboardNative.CloseClipboard(); }
    }

    private static string? ReadText()
    {
        if (!OpenWithRetry()) return null;
        try
        {
            var h = ClipboardNative.GetClipboardData(13); // CF_UNICODETEXT
            if (h == 0) return null;
            var p = ClipboardNative.GlobalLock(h);
            if (p == 0) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { ClipboardNative.GlobalUnlock(h); }
        }
        finally { ClipboardNative.CloseClipboard(); }
    }

    private static void Restore(List<(uint Format, byte[] Data)> saved, FileLog log)
    {
        if (!OpenWithRetry()) { log.Error("selection: couldn't reopen the clipboard to restore it"); return; }
        try
        {
            ClipboardNative.EmptyClipboard();
            foreach (var (format, data) in saved)
            {
                var h = ClipboardNative.GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (nuint)Math.Max(1, data.Length));
                if (h == 0) continue;
                var p = ClipboardNative.GlobalLock(h);
                if (p != 0)
                {
                    Marshal.Copy(data, 0, p, data.Length);
                    ClipboardNative.GlobalUnlock(h);
                }
                if (ClipboardNative.SetClipboardData(format, h) == 0) ClipboardNative.GlobalFree(h); // on success the system owns it
            }
        }
        finally { ClipboardNative.CloseClipboard(); }
    }

    private static bool OpenWithRetry()
    {
        for (var i = 0; i < 10; i++)
        {
            if (ClipboardNative.OpenClipboard(0)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    private static void SendCtrlC()
    {
        var inputs = new[]
        {
            ClipboardNative.Key(0x11, up: false), ClipboardNative.Key(0x43, up: false),
            ClipboardNative.Key(0x43, up: true), ClipboardNative.Key(0x11, up: true),
        };
        ClipboardNative.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<ClipboardNative.INPUT>());
    }
}

internal static class ClipboardNative
{
    [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] public static extern bool CloseClipboard();
    [DllImport("user32.dll")] public static extern bool EmptyClipboard();
    [DllImport("user32.dll")] public static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] public static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] public static extern nint SetClipboardData(uint format, nint mem);
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
