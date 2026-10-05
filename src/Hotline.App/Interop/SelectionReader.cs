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
/// <see cref="AttachSelectionMode.Clipboard"/> also Ctrl+C, but only where UI Automation answered that the focused
/// control has no text pattern (never after a timeout, for an empty selection, in a password field or a terminal).
/// The caller's wait is bounded, so opening the panel never waits long.
/// </summary>
internal static class SelectionReader
{
    private const int UIA_TextPatternId = 10014;
    private const int UIA_IsPasswordPropertyId = 30019;
    private static readonly TimeSpan UiaBudget = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan ClipboardBudget = TimeSpan.FromMilliseconds(450);
    private static readonly HashSet<string> ConsoleClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "PseudoConsoleWindow", "mintty", "VirtualConsoleClass",
    };

    // One UI Automation client for all reads, with short timeouts so a hung app can't hold threads for ~20 s.
    private static readonly Lazy<IUIAutomation> Automation = new(() =>
    {
        var automation = new CUIAutomation8();
        if (automation is IUIAutomation2 timed)
        {
            timed.ConnectionTimeout = 300;
            timed.TransactionTimeout = 300;
        }
        return automation;
    });
    private static int _uiaBusy;

    public sealed record Result(string Text, string? App, string Via);

    private enum UiaOutcome { Text, NoTextPattern, Password, Nothing }

    /// <summary>
    /// Blocks at most ~150 ms (~600 ms with Ctrl+C; the clipboard is put back in the background afterwards). Null when
    /// nothing is selected, it can't be read, or focus is in a password field.
    /// </summary>
    public static Result? Read(nint foreground, AttachSelectionMode mode, FileLog log)
    {
        if (mode == AttachSelectionMode.Off || foreground == 0) return null;
        Native.GetWindowThreadProcessId(foreground, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return null;
        if (Interlocked.Exchange(ref _uiaBusy, 1) == 1) { log.Info("selection: previous read still running; skipped"); return null; }
        var frameHost = IsFrameHost(pid); // UWP: the frame window's process isn't the app's
        var sw = Stopwatch.StartNew();
        string? text = null, via = null;
        var outcome = UiaOutcome.Nothing;
        // UIA calls go into the other process: run them off the UI thread (thread pool = MTA) and stop waiting in time.
        var uia = Task.Run(() =>
        {
            try { return ReadUia(pid, frameHost); }
            finally { Volatile.Write(ref _uiaBusy, 0); }
        });
        try
        {
            if (uia.Wait(UiaBudget)) (outcome, text, via) = (uia.Result.Outcome, uia.Result.Text, "UI Automation");
            else log.Info("selection: UI Automation didn't answer in time");
        }
        catch (AggregateException ex) { log.Debug($"selection: UI Automation failed: {ex.InnerException?.Message}"); }

        // Ctrl+C only where UIA positively said "no text pattern here": an empty selection would copy a whole line
        // (editors) or interrupt a command (terminals), and a timeout can't rule out a password field.
        if (outcome == UiaOutcome.NoTextPattern && mode == AttachSelectionMode.Clipboard && !ConsoleClasses.Contains(Native.ClassNameOf(foreground)))
        {
            var copied = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var stop = new CancellationTokenSource();
            var deadline = Environment.TickCount64 + (long)ClipboardBudget.TotalMilliseconds;
            var token = stop.Token;
            _ = Task.Run(() =>
            {
                try { ClipboardCopy.CopySelection(foreground, deadline, token, copied, log); }
                catch (Exception ex) { log.Error("selection: Ctrl+C copy failed", ex); }
                finally { copied.TrySetResult(null); }
            });
            if (copied.Task.Wait(ClipboardBudget + TimeSpan.FromMilliseconds(150))) (text, via) = (copied.Task.Result, "Ctrl+C");
            else log.Error("selection: Ctrl+C copy didn't finish in time");
            stop.Cancel(); // never send Ctrl+C once the panel may have focus
        }
        if (string.IsNullOrWhiteSpace(text)) return null;
        log.Info($"selection: {text.Length} chars via {via} in {sw.ElapsedMilliseconds} ms");
        return new Result(text, AppName(foreground, pid), via!);
    }

    private static bool IsFrameHost(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return string.Equals(p.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }

    private static (UiaOutcome Outcome, string? Text) ReadUia(uint pid, bool frameHost)
    {
        var element = Automation.Value.GetFocusedElement();
        if (element is null) return (UiaOutcome.Nothing, null);
        var owner = element.CurrentProcessId;
        // Focus already moved on (to us, or elsewhere). A UWP app's element lives in its own process, not the frame host's.
        if (owner == Environment.ProcessId || (owner != (int)pid && !frameHost)) return (UiaOutcome.Nothing, null);
        if (element.GetCurrentPropertyValue(UIA_IsPasswordPropertyId) is true) return (UiaOutcome.Password, null);
        if (element.GetCurrentPattern(UIA_TextPatternId) is not IUIAutomationTextPattern pattern) return (UiaOutcome.NoTextPattern, null);
        var ranges = pattern.GetSelection();
        if (ranges is null) return (UiaOutcome.Text, null);
        var sb = new StringBuilder();
        for (var i = 0; i < ranges.Length && sb.Length <= SelectionAttachment.MaxChars; i++)
        {
            var piece = ranges.GetElement(i).GetText(SelectionAttachment.MaxChars + 1);
            if (string.IsNullOrEmpty(piece)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(piece);
        }
        return (UiaOutcome.Text, sb.ToString());
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
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    /// <summary>
    /// Sends Ctrl+C to <paramref name="target"/> (only while it's still the foreground window, before
    /// <paramref name="deadline"/> and before <paramref name="stop"/>), hands the copied text to <paramref name="copied"/>
    /// as soon as it's read (the panel opens then), and then puts the previous clipboard back. Runs on a worker thread;
    /// the clipboard is opened with <see cref="ClipboardOwner"/>'s window, whose thread keeps pumping messages (a null
    /// owner would make every SetClipboardData fail after EmptyClipboard).
    /// </summary>
    public static void CopySelection(nint target, long deadline, CancellationToken stop, TaskCompletionSource<string?> copied, FileLog log)
    {
        if (!OneAtATime.Wait(0)) { log.Info("selection: a Ctrl+C copy is still running; skipped"); return; }
        try
        {
            if (ClipboardOwner.Hwnd == 0) { log.Error("selection: no clipboard owner window; skipped Ctrl+C"); return; }
            // The key's own modifiers (Win/Shift for the Copilot key) must be up, or Ctrl+C becomes another shortcut.
            // While the key is held (push-to-talk) they stay down: skip at once instead of waiting.
            if (ModifiersDown()) { log.Info("selection: modifiers held; skipped Ctrl+C"); return; }
            var saved = Snapshot(log);
            if (saved is null) return;
            if (stop.IsCancellationRequested || Environment.TickCount64 > deadline || Native.GetForegroundWindow() != target)
            {
                log.Info("selection: too late or focus moved; skipped Ctrl+C");
                return;
            }
            var before = ClipboardNative.GetClipboardSequenceNumber();
            SendCtrlC();
            // Wait for the copy only until the deadline: after that the panel is open, and a clipboard change is more
            // likely the user's own copy, which must not be "restored" away.
            while (ClipboardNative.GetClipboardSequenceNumber() == before && Environment.TickCount64 < deadline && !stop.IsCancellationRequested) Thread.Sleep(10);
            if (ClipboardNative.GetClipboardSequenceNumber() == before) return; // nothing was copied: clipboard untouched
            Thread.Sleep(20); // let the app finish writing all of its formats
            var ours = ClipboardNative.GetClipboardSequenceNumber();
            try { copied.TrySetResult(IsWholeLineCopy() ? null : ReadText()); }
            finally { Restore(saved, ours, log); }
        }
        finally { OneAtATime.Release(); }
    }

    private static bool ModifiersDown() =>
        new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(vk => (ClipboardNative.GetAsyncKeyState(vk) & 0x8000) != 0); // Shift, Ctrl, Alt, LWin, RWin

    private static bool IsHandleFormat(uint f) =>
        f is 2 or 3 or 9 or 14 or 0x80 or 0x82 or 0x83 or 0x8E || f is >= 0x200 and <= 0x3FF; // bitmap, metafile, palette, owner/private/GDI

    private static List<(uint Format, byte[] Data)>? Snapshot(FileLog log)
    {
        if (!OpenWithRetry(TimeSpan.FromMilliseconds(100))) { log.Info("selection: clipboard busy; skipped Ctrl+C"); return null; }
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

    /// <summary>Visual Studio and VS Code copy the whole line when nothing is selected; that isn't a selection.</summary>
    private static bool IsWholeLineCopy()
    {
        if (ClipboardNative.IsClipboardFormatAvailable(ClipboardNative.RegisterClipboardFormat("MSDEVLineSelect"))
            || ClipboardNative.IsClipboardFormatAvailable(ClipboardNative.RegisterClipboardFormat("VisualStudioEditorOperationsLineCutCopyClipboardTag")))
            return true;
        var vscode = ClipboardNative.RegisterClipboardFormat("vscode-editor-data");
        if (!ClipboardNative.IsClipboardFormatAvailable(vscode)) return false;
        var json = ReadString(vscode, unicode: false);
        return json?.Contains("\"isFromEmptySelection\":true", StringComparison.Ordinal) == true;
    }

    private static string? ReadText() => ReadString(13, unicode: true); // CF_UNICODETEXT

    /// <summary>Reads a text format, bounded by the memory block's size (some apps omit the terminating null).</summary>
    private static string? ReadString(uint format, bool unicode)
    {
        if (!OpenWithRetry(TimeSpan.FromMilliseconds(200))) return null;
        try
        {
            var h = ClipboardNative.GetClipboardData(format);
            if (h == 0) return null;
            var size = (long)ClipboardNative.GlobalSize(h);
            var p = ClipboardNative.GlobalLock(h);
            if (p == 0) return null;
            try
            {
                var s = unicode ? Marshal.PtrToStringUni(p, (int)Math.Min(size / 2, int.MaxValue)) : Marshal.PtrToStringUTF8(p, (int)Math.Min(size, int.MaxValue));
                var end = s.IndexOf('\0');
                return end >= 0 ? s[..end] : s;
            }
            finally { ClipboardNative.GlobalUnlock(h); }
        }
        finally { ClipboardNative.CloseClipboard(); }
    }

    /// <summary>
    /// Puts the saved formats back (retrying while clipboard managers hold it open), marked so clipboard history and
    /// cloud clipboard don't record the restore. Skipped when someone else wrote to the clipboard after our copy.
    /// </summary>
    private static void Restore(List<(uint Format, byte[] Data)> saved, uint ours, FileLog log)
    {
        if (!OpenWithRetry(TimeSpan.FromSeconds(2))) { log.Error("selection: couldn't reopen the clipboard to restore it"); return; }
        try
        {
            if (ClipboardNative.GetClipboardSequenceNumber() != ours) { log.Info("selection: clipboard changed again; not restored"); return; }
            ClipboardNative.EmptyClipboard();
            foreach (var (format, data) in saved) Put(format, data);
            Put(ClipboardNative.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), [0]);
            Put(ClipboardNative.RegisterClipboardFormat("CanIncludeInClipboardHistory"), [0, 0, 0, 0]);
            Put(ClipboardNative.RegisterClipboardFormat("CanUploadToCloudClipboard"), [0, 0, 0, 0]);
        }
        finally { ClipboardNative.CloseClipboard(); }
    }

    private static void Put(uint format, byte[] data)
    {
        if (format == 0) return;
        var h = ClipboardNative.GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (nuint)Math.Max(1, data.Length));
        if (h == 0) return;
        var p = ClipboardNative.GlobalLock(h);
        if (p != 0)
        {
            Marshal.Copy(data, 0, p, data.Length);
            ClipboardNative.GlobalUnlock(h);
        }
        if (ClipboardNative.SetClipboardData(format, h) == 0) ClipboardNative.GlobalFree(h); // on success the system owns it
    }

    private static bool OpenWithRetry(TimeSpan patience)
    {
        var until = Environment.TickCount64 + (long)patience.TotalMilliseconds;
        do
        {
            if (ClipboardNative.OpenClipboard(ClipboardOwner.Hwnd)) return true;
            Thread.Sleep(10);
        } while (Environment.TickCount64 < until);
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

/// <summary>
/// A message-only window on its own thread that owns the clipboard while Hotline restores it. Its thread always pumps
/// messages, so other apps' clipboard calls (WM_DESTROYCLIPBOARD to the previous owner) never wait on Hotline's UI thread.
/// </summary>
internal static class ClipboardOwner
{
    private static readonly Lazy<nint> Window = new(Start);

    public static nint Hwnd => Window.Value;

    private static nint Start()
    {
        var ready = new TaskCompletionSource<nint>();
        var thread = new Thread(() =>
        {
            // The system STATIC class needs no window procedure of ours; HWND_MESSAGE (-3) keeps it invisible.
            var hwnd = ClipboardNative.CreateWindowEx(0, "STATIC", "Hotline clipboard", 0, 0, 0, 0, 0, -3, 0, 0, 0);
            ready.SetResult(hwnd);
            if (hwnd == 0) return;
            while (ClipboardNative.GetMessage(out var msg, 0, 0, 0) > 0)
            {
                ClipboardNative.TranslateMessage(ref msg);
                ClipboardNative.DispatchMessage(ref msg);
            }
        }) { IsBackground = true, Name = "Hotline clipboard owner" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.Result;
    }
}

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
