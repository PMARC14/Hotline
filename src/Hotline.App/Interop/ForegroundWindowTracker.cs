using System.Runtime.InteropServices;
using System.Text;
using Hotline.Core.Diagnostics;
using Hotline.Core.Windowing;

namespace Hotline.App.Interop;

/// <summary>
/// Tracks the last real foreground application window via <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c>
/// on the UI message-loop thread so popup monitor placement and window capture still know which window
/// the user was in after Explorer's <c>CopilotKeyPressFocusFrame</c> briefly takes focus.
/// </summary>
public sealed class ForegroundWindowTracker : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;
    private const uint GW_OWNER = 4;

    private delegate void WinEventDelegate(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(nint hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    private readonly FileLog _log;
    private readonly WinEventDelegate _callback;
    private nint _hook;
    private int _suppressed;
    private WindowCandidate _remembered;
    private WindowCandidate _showTarget;

    public Func<uint, bool>? IsOurProcess { get; set; }

    public ForegroundWindowTracker(FileLog log, Func<uint, bool>? isOurProcess = null)
    {
        _log = log;
        IsOurProcess = isOurProcess;
        _callback = OnWinEvent;
        try { TryRemember(Native.GetForegroundWindow()); }
        catch (Exception ex) { _log.Error("seeding foreground window tracker failed", ex); }

        _hook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND,
            EVENT_SYSTEM_FOREGROUND,
            0,
            _callback,
            0,
            0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (_hook == 0)
            _log.Error("could not register EVENT_SYSTEM_FOREGROUND hook");
    }

    /// <summary>Suppresses foreground tracking while a modal dialog, file picker or region picker is active.</summary>
    public IDisposable Suppress()
    {
        Interlocked.Increment(ref _suppressed);
        return new Releaser(() => Interlocked.Decrement(ref _suppressed));
    }

    /// <summary>Resolves the target window when the popup opens, preferring a real live foreground window over the revalidated remembered one.</summary>
    public nint ResolveForShow()
    {
        var live = Snapshot(Native.GetForegroundWindow());
        var remembered = _remembered.Handle != 0 ? Snapshot(_remembered.Handle, _remembered) : default;
        if (!ForegroundWindowPolicy.IsReal(remembered))
            _remembered = default;

        var chosen = ForegroundWindowPolicy.Choose(live, remembered);
        if (chosen != 0)
        {
            var winner = chosen == live.Handle && ForegroundWindowPolicy.IsReal(live) ? live : remembered;
            _remembered = winner;
            _showTarget = winner;
        }
        else
        {
            _showTarget = default;
        }
        return chosen;
    }

    /// <summary>Revalidates a show-time target window (identity, existence, visibility, non-minimized, non-cloaked) before use.</summary>
    public nint RevalidateShowTarget(nint hwnd)
    {
        if (hwnd == 0)
        {
            _showTarget = default;
            return 0;
        }
        var expected = _showTarget.Handle == hwnd ? _showTarget
            : _remembered.Handle == hwnd ? _remembered
            : (WindowCandidate?)null;
        var candidate = Snapshot(hwnd, expected);
        if (ForegroundWindowPolicy.IsReal(candidate))
        {
            _showTarget = candidate;
            return hwnd;
        }
        if (_showTarget.Handle == hwnd) _showTarget = default;
        if (_remembered.Handle == hwnd) _remembered = default;
        return 0;
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (_suppressed > 0 || eventType != EVENT_SYSTEM_FOREGROUND || idObject != OBJID_WINDOW || hwnd == 0) return;
        try { TryRemember(hwnd); }
        catch (Exception ex) { _log.Error("foreground window hook callback failed", ex); }
    }

    private void TryRemember(nint hwnd)
    {
        var candidate = Snapshot(hwnd);
        _remembered = ForegroundWindowPolicy.UpdateRemembered(candidate, _remembered);
    }

    private WindowCandidate Snapshot(nint hwnd, WindowCandidate? expectedIdentity = null)
    {
        if (hwnd == 0) return default;
        var exists = Native.IsWindow(hwnd);
        if (!exists) return new WindowCandidate(hwnd, Exists: false);

        var tid = Native.GetWindowThreadProcessId(hwnd, out var pid);
        var identityMatches = tid != 0 && pid != 0 &&
            (expectedIdentity is not { } expected || (expected.Handle == hwnd && expected.ProcessId == pid && expected.ThreadId == tid));
        var isHotline = IsHotlineProcess(pid);
        var visible = Native.IsWindowVisible(hwnd);
        var minimized = Native.IsIconic(hwnd);
        var cloaked = Native.DwmGetWindowAttributeInt(hwnd, Native.DWMWA_CLOAKED, out var c, sizeof(int)) == 0 && c != 0;
        var isOwnedDialog = IsHotlineOwnedDialog(hwnd);
        var className = Native.ClassNameOf(hwnd);

        return new WindowCandidate(
            Handle: hwnd,
            Exists: exists,
            IdentityMatches: identityMatches,
            IsHotline: isHotline,
            Visible: visible,
            Minimized: minimized,
            Cloaked: cloaked,
            IsOwnedDialog: isOwnedDialog,
            ClassName: className,
            ProcessId: pid,
            ThreadId: tid);
    }

    private bool IsHotlineProcess(uint pid)
    {
        if (pid == 0) return false;
        if (pid == (uint)Environment.ProcessId) return true;
        if (IsOurProcess?.Invoke(pid) == true) return true;
        return IsHotlineExecutable(pid);
    }

    internal static bool IsHotlineExecutable(uint pid)
    {
        var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0) return false;
        try
        {
            var sb = new StringBuilder(512);
            var size = sb.Capacity;
            if (!QueryFullProcessImageName(handle, 0, sb, ref size) || size <= 0) return false;
            var name = Path.GetFileNameWithoutExtension(sb.ToString());
            return string.Equals(name, "Hotline", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private bool IsHotlineOwnedDialog(nint hwnd)
    {
        if (_suppressed > 0 && hwnd != _showTarget.Handle && hwnd != _remembered.Handle)
            return true;
        var cur = hwnd;
        for (var depth = 0; depth < 8 && cur != 0; depth++)
        {
            var owner = GetWindow(cur, GW_OWNER);
            if (owner == 0) break;
            Native.GetWindowThreadProcessId(owner, out var ownerPid);
            if (IsHotlineProcess(ownerPid)) return true;
            cur = owner;
        }
        return false;
    }

    public void Dispose()
    {
        var hook = Interlocked.Exchange(ref _hook, 0);
        if (hook != 0) UnhookWinEvent(hook);
        GC.KeepAlive(_callback);
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
