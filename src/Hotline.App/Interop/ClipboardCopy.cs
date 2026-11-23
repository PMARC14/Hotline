using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;
using Interop.UIAutomationClient;

namespace Hotline.App.Interop;

internal static class ClipboardCopy
{
    private const long MaxSnapshotBytes = 64L * 1024 * 1024;
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);
    private sealed record ClipboardSnapshot(List<(uint Format, byte[] Data)> Formats, uint Sequence);

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
            Native.GetWindowThreadProcessId(target, out var targetProcess);
            if (targetProcess == 0 || !ClipboardCopyPolicy.MaySend(Environment.TickCount64, deadline,
                stop.IsCancellationRequested, Native.GetForegroundWindow() == target,
                saved.Sequence, ClipboardNative.GetClipboardSequenceNumber()))
            {
                log.Info("selection: too late or focus moved; skipped Ctrl+C");
                return;
            }
            var before = saved.Sequence;
            SendCtrlC();
            // Cancellation prevents sending keys, but must not cancel cleanup of a copy already sent.
            while (true)
            {
                var now = Environment.TickCount64;
                if (now >= deadline) copied.TrySetResult(null); // panel opens while cleanup continues
                var action = ClipboardCopyPolicy.Observe(now, deadline, before,
                    ClipboardNative.GetClipboardSequenceNumber(), targetProcess, OwnerProcess());
                if (action == ClipboardCopyAction.Stop) return;
                if (action == ClipboardCopyAction.Wait) { Thread.Sleep(10); continue; }

                Thread.Sleep(20); // let the app finish writing all of its formats
                var ours = ClipboardNative.GetClipboardSequenceNumber();
                try
                {
                    if (action == ClipboardCopyAction.ReadAndRestore)
                        copied.TrySetResult(ReadCopiedText(ours, targetProcess, deadline, out ours));
                }
                finally { Restore(saved.Formats, ours, targetProcess, log); }
                return;
            }
        }
        finally { OneAtATime.Release(); }
    }

    private static bool ModifiersDown() =>
        new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(vk => (ClipboardNative.GetAsyncKeyState(vk) & 0x8000) != 0); // Shift, Ctrl, Alt, LWin, RWin

    private static ClipboardSnapshot? Snapshot(FileLog log)
    {
        if (!OpenWithRetry(TimeSpan.FromMilliseconds(100))) { log.Info("selection: clipboard busy; skipped Ctrl+C"); return null; }
        try
        {
            var formats = new List<(uint, byte[])>();
            long total = 0;
            uint f = 0;
            while (true)
            {
                f = ClipboardNative.EnumClipboardFormats(f);
                if (f == 0)
                {
                    if (Marshal.GetLastWin32Error() != 0) return null;
                    break;
                }
                var name = new StringBuilder(256);
                if (f >= 0xC000) ClipboardNative.GetClipboardFormatName(f, name, name.Capacity);
                if (!ClipboardCopyPolicy.CanSnapshotFormat(f, name.ToString()))
                {
                    log.Info("selection: clipboard has formats that cannot be safely restored; skipped Ctrl+C");
                    return null;
                }
                var h = ClipboardNative.GetClipboardData(f);
                if (h == 0) return null;
                var size = (long)ClipboardNative.GlobalSize(h);
                if (size <= 0) return null;
                if ((total += size) > MaxSnapshotBytes) { log.Info("selection: clipboard too large to restore; skipped Ctrl+C"); return null; }
                var p = ClipboardNative.GlobalLock(h);
                if (p == 0) return null;
                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(p, bytes, 0, (int)size);
                    formats.Add((f, bytes));
                }
                finally { ClipboardNative.GlobalUnlock(h); }
            }
            return new ClipboardSnapshot(formats, ClipboardNative.GetClipboardSequenceNumber());
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

    private static uint OwnerProcess()
    {
        Native.GetWindowThreadProcessId(ClipboardNative.GetClipboardOwner(), out var process);
        return process;
    }

    private static string? ReadCopiedText(uint sequence, uint targetProcess, long deadline, out uint restoreSequence)
    {
        restoreSequence = sequence;
        if (!OpenWithRetry(TimeSpan.FromMilliseconds(200))) return null;
        try
        {
            if (!ClipboardCopyPolicy.MayRead(Environment.TickCount64, deadline, sequence,
                ClipboardNative.GetClipboardSequenceNumber(), targetProcess, OwnerProcess())) return null;
            try { return IsWholeLineCopy() ? null : ReadText(); }
            finally
            {
                // GetClipboardData can cause delayed rendering and advance the sequence while we hold the lock.
                restoreSequence = ClipboardNative.GetClipboardSequenceNumber();
            }
        }
        finally { ClipboardNative.CloseClipboard(); }
    }

    /// <summary>Reads a text format, bounded by the memory block's size (some apps omit the terminating null).</summary>
    private static string? ReadString(uint format, bool unicode)
    {
        // Caller holds the clipboard open and has checked its owner and sequence.
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

    /// <summary>
    /// Puts the saved formats back (retrying while clipboard managers hold it open), marked so clipboard history and
    /// cloud clipboard don't record the restore. Skipped when someone else wrote to the clipboard after our copy.
    /// </summary>
    private static void Restore(List<(uint Format, byte[] Data)> saved, uint ours, uint targetProcess, FileLog log)
    {
        if (!OpenWithRetry(TimeSpan.FromSeconds(2))) { log.Error("selection: couldn't reopen the clipboard to restore it"); return; }
        try
        {
            if (!ClipboardCopyPolicy.MayRestore(ours, ClipboardNative.GetClipboardSequenceNumber(), targetProcess, OwnerProcess()))
            { log.Info("selection: clipboard changed again or has another owner; not restored"); return; }
            if (!ClipboardNative.EmptyClipboard()) { log.Error("selection: couldn't empty the clipboard to restore it"); return; }
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
        if (p == 0) { ClipboardNative.GlobalFree(h); return; } // never hand the clipboard memory we couldn't fill
        Marshal.Copy(data, 0, p, data.Length);
        ClipboardNative.GlobalUnlock(h);
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
