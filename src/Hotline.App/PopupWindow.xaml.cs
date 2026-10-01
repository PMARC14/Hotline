using Hotline.App.Interop;
using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using WindowActivatedEventArgs = Microsoft.UI.Xaml.WindowActivatedEventArgs;

namespace Hotline.App;

/// <summary>Native acrylic chat panel. Created hidden at startup so showing it is instant.</summary>
public sealed partial class PopupWindow : Window
{
    private readonly WindowSettings _settings;
    private readonly GrowMode _growMode;
    private readonly PopupToggleGuard _guard;
    private readonly FileLog _log;
    private int _modal;
    private RectI _bar;
    private RectI _work;
    private double _scale = 1;
    private int _contentPx;

    public nint Hwnd { get; }
    /// <summary>The window the user was in before the popup appeared (target for monitor choice and capture).</summary>
    public nint PreviousForeground { get; private set; }
    /// <summary>Pinned: stays open when it loses focus (so files can be dragged in from Explorer).</summary>
    public bool Pinned { get; set; }

    public event Action? Shown;
    public event Action? NewChatRequested;
    public event Action<bool>? CaptureRequested;

    public PopupWindow(WindowSettings settings, GrowMode growMode, PopupToggleGuard guard, FileLog log, bool showDebugStatus)
    {
        _settings = settings;
        _growMode = growMode;
        _guard = guard;
        _log = log;
        InitializeComponent();
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        SystemBackdrop = Backdrops.Create(settings);
        ExtendsContentIntoTitleBar = true;

        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = settings.AlwaysOnTop;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon("Assets\\Hotline.ico");
        AppWindow.Closing += (_, e) => { e.Cancel = true; HidePopup(); };

        Root.RequestedTheme = settings.Theme switch
        {
            ThemeChoice.Light => ElementTheme.Light,
            ThemeChoice.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        if (settings.Backdrop == BackdropKind.Solid)
            Root.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        if (showDebugStatus)
            StatusText.Visibility = Visibility.Visible;
        MessagesScroll.VerticalScrollBarVisibility = settings.Scrollbar switch
        {
            ScrollbarStyle.Visible => Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Visible,
            ScrollbarStyle.Hidden => Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden,
            _ => Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
        };

        Activated += OnActivated;
    }

    public bool IsShown => AppWindow.IsVisible;

    public void ShowPopup()
    {
        var fg = Native.GetForegroundWindow();
        if (fg != Hwnd && fg != 0 && !ShellSurfaces.IsShell(Native.ClassNameOf(fg)))
            PreviousForeground = fg; // keep the last real app window; taskbar/desktop/flyouts don't count

        PlaceOnActiveMonitor();
        Activate();
        Native.SetForegroundWindow(Hwnd);
        Shown?.Invoke();
    }

    public void HidePopup()
    {
        if (!AppWindow.IsVisible) return;
        AppWindow.Hide();
        _guard.NoteHidden();
    }

    /// <summary>Hides for a system dialog (file picker) without counting as a user dismissal.</summary>
    public void HideForDialog() => AppWindow.Hide();

    public void Toggle()
    {
        if (_guard.ShouldShowOnToggle(AppWindow.IsVisible)) ShowPopup();
        else HidePopup();
    }

    public void SetStatus(string text) => StatusText.Text = text;

    public void RequestNewChat() => NewChatRequested?.Invoke();
    public void RequestCapture(bool window) => CaptureRequested?.Invoke(window);

    /// <summary>Content height (DIPs) of the conversation; the panel grows upward from its baseline.</summary>
    public void SetContentHeight(double dip)
    {
        _contentPx = (int)Math.Ceiling(dip * _scale);
        if (AppWindow.IsVisible) ApplyHeight();
    }

    /// <summary>While held, focus loss (dialogs, captures) does not hide the popup.</summary>
    public IDisposable Modal()
    {
        _modal++;
        return new Releaser(() => _modal--);
    }

    /// <summary>Hides the popup briefly (e.g. to capture what is underneath), then brings it back.</summary>
    public async Task<T> WithHiddenAsync<T>(Func<Task<T>> work)
    {
        using var _ = Modal();
        AppWindow.Hide();
        await Task.Delay(220); // let DWM repaint without the popup
        try { return await work(); }
        finally
        {
            Activate();
            Native.SetForegroundWindow(Hwnd);
            Shown?.Invoke();
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated && _settings.HideOnBlur && _modal == 0 && !Pinned)
            HidePopup();
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            HidePopup();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.N && InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down)
                 && !InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down))
        {
            RequestNewChat();
            e.Handled = true;
        }
    }

    private void ApplyHeight()
    {
        if (_bar.Width == 0) return;
        var maxPx = (int)Math.Round(_work.Height * _settings.MaxHeightPercent / 100.0);
        var r = PopupGeometry.GrowUp(_bar, _contentPx, maxPx, _work, _growMode);
        AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
    }

    private void PlaceOnActiveMonitor()
    {
        // A remembered window may have closed since (stale handle): its monitor/DPI lookups then fail and the
        // popup came out at 100% scale ("occasionally small"). Only trust live windows; otherwise use the cursor.
        if (PreviousForeground != 0 && !Native.IsWindow(PreviousForeground))
            PreviousForeground = 0;
        var anchor = PreviousForeground;
        DisplayArea? area = null;
        nint monitor = 0;
        if (anchor != 0)
        {
            area = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(anchor), DisplayAreaFallback.Nearest);
            monitor = Native.MonitorFromWindow(anchor, Native.MONITOR_DEFAULTTONEAREST);
        }
        if (area is null || monitor == 0)
        {
            if (anchor != 0) _log.Error($"no display area for previous foreground window 0x{anchor:X} ({Native.ClassNameOf(anchor)}); using cursor monitor");
            Native.GetCursorPos(out var pt);
            area = DisplayArea.GetFromPoint(new PointInt32(pt.X, pt.Y), DisplayAreaFallback.Nearest) ?? DisplayArea.Primary;
            monitor = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
        }
        var wa = area.WorkArea;
        var scale = Native.GetDpiForMonitor(monitor, Native.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;

        var widthDip = PopupGeometry.RelativeWidthDip(wa.Width, scale, _settings.WidthPercent, _settings.MinWidth, _settings.MaxWidth);
        var r = PopupGeometry.Place(new RectI(wa.X, wa.Y, wa.Width, wa.Height), widthDip, _settings.Height, scale, _settings.VerticalPosition);
        _log.Debug($"place: anchor=0x{anchor:X} ({Native.ClassNameOf(anchor)}) workArea={wa.X},{wa.Y} {wa.Width}x{wa.Height} scale={scale} -> {r}");
        (_bar, _work, _scale) = (r, new RectI(wa.X, wa.Y, wa.Width, wa.Height), scale);
        ApplyHeight();
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
