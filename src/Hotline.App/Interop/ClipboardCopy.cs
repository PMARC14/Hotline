using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Interop.UIAutomationClient;

namespace Hotline.App.Interop;

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
