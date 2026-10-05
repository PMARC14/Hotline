using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Interop.UIAutomationClient;

namespace Hotline.App.Interop;

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
