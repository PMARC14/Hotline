using System.Runtime.InteropServices;

namespace Hotline.App.Interop;

/// <summary>Native notification-area icon: left click toggles the popup, right click opens a menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint WM_TRAY = Native.WM_APP + 2;
    private const uint CmdOpen = 1, CmdSettings = 2, CmdRestart = 3, CmdQuit = 4;

    private readonly WindowMessageHook _hook;
    private readonly Dictionary<uint, Action> _commands;
    private Native.NOTIFYICONDATA _data;
    private long _lastMenuMs = -1000;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _retry;
    private bool _added, _version4, _disposed;

    public TrayIcon(WindowMessageHook hook, string iconPath, Action onToggle, Action onOpenSettings, Action onRestart, Action onQuit)
    {
        _hook = hook;
        _commands = new() { [CmdOpen] = onToggle, [CmdSettings] = onOpenSettings, [CmdRestart] = onRestart, [CmdQuit] = onQuit };
        _data = new Native.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<Native.NOTIFYICONDATA>(),
            hWnd = hook.Hwnd,
            uID = 1,
            uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP,
            uCallbackMessage = WM_TRAY,
            // Pick the multi-size .ico entry matching the tray size at this display scale (e.g. 32 px at 200%).
            hIcon = Native.LoadImage(0, iconPath, Native.IMAGE_ICON, TraySize(hook.Hwnd), TraySize(hook.Hwnd), Native.LR_LOADFROMFILE),
            szTip = "Hotline",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
            uVersion = Native.NOTIFYICON_VERSION_4,
        };
        _retry = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _retry.Interval = TimeSpan.FromSeconds(2);
        _retry.Tick += (_, _) => Add();
        Add();

        hook.On(WM_TRAY, (_, lParam) =>
        {
            if (_disposed) return true;
            switch ((uint)(lParam & 0xFFFF)) // NOTIFYICON_VERSION_4: LOWORD(lParam) = event
            {
                // Version 4 emits NIN_SELECT for mouse activation too: don't toggle twice.
                case Native.NIN_SELECT or Native.NIN_KEYSELECT: onToggle(); break;
                case Native.WM_LBUTTONUP when !_version4: onToggle(); break;
                // Version 4 sends WM_CONTEXTMENU after WM_RBUTTONUP for one right-click (and alone for the keyboard): one
                // menu per click, whichever arrives first.
                case Native.WM_RBUTTONUP or Native.WM_CONTEXTMENU:
                    if (Environment.TickCount64 - _lastMenuMs > 500) ShowMenu();
                    _lastMenuMs = Environment.TickCount64; // after the menu closes: the click's second message is now stale too
                    break;
            }
            return true;
        });
        hook.On(Native.WM_COMMAND, (wParam, _) =>
        {
            if (!_commands.TryGetValue((uint)(wParam & 0xFFFF), out var action)) return false;
            action();
            return true;
        });
        // Explorer restarts drop tray icons; re-add when the taskbar comes back.
        hook.On(Native.RegisterWindowMessage("TaskbarCreated"), (_, _) => { _added = _version4 = false; Add(); return false; });
    }

    private static int TraySize(nint hwnd)
    {
        var dpi = Native.GetDpiForWindow(hwnd);
        return Native.GetSystemMetricsForDpi(Native.SM_CXSMICON, dpi == 0 ? 96 : dpi);
    }

    private void Add()
    {
        if (_disposed) return;
        if (!_added) _added = Native.Shell_NotifyIcon(Native.NIM_ADD, ref _data);
        _version4 = _added && Native.Shell_NotifyIcon(Native.NIM_SETVERSION, ref _data);
        if (_version4) _retry.Stop();
        else _retry.Start();
    }

    private void ShowMenu()
    {
        var menu = Native.CreatePopupMenu();
        Native.AppendMenu(menu, Native.MF_STRING, CmdOpen, Strings.Tray_Open);
        Native.AppendMenu(menu, Native.MF_STRING, CmdSettings, Strings.Tray_Settings);
        Native.AppendMenu(menu, Native.MF_STRING, CmdRestart, Strings.Tray_Restart);
        Native.AppendMenu(menu, Native.MF_SEPARATOR, 0, null);
        Native.AppendMenu(menu, Native.MF_STRING, CmdQuit, Strings.Tray_Quit);
        Native.GetCursorPos(out var pt);
        Native.SetForegroundWindow(_hook.Hwnd); // required so the menu closes when clicking elsewhere
        Native.TrackPopupMenuEx(menu, Native.TPM_RIGHTBUTTON | Native.TPM_BOTTOMALIGN, pt.X, pt.Y, _hook.Hwnd, 0);
        Native.PostMessage(_hook.Hwnd, Native.WM_NULL, 0, 0);
        Native.DestroyMenu(menu);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _retry.Stop();
        Native.Shell_NotifyIcon(Native.NIM_DELETE, ref _data);
        if (_data.hIcon == 0) return;
        Native.DestroyIcon(_data.hIcon);
        _data.hIcon = 0;
    }
}
