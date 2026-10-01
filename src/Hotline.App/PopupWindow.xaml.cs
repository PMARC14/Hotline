using Hotline.App.Interop;
using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace Hotline.App;

/// <summary>Centered acrylic popup. Created hidden at startup so showing it is instant.</summary>
public sealed partial class PopupWindow : Window
{
    private readonly WindowSettings _settings;
    private readonly PopupToggleGuard _guard;
    private readonly FileLog _log;
    private readonly int _maxHeightDip;
    private int _modal;
    private RectI _bar;
    private RectI _work;
    private double _scale = 1;
    private int _contentPx;

    public nint Hwnd { get; }
    /// <summary>The window the user was in before the popup appeared (target for monitor choice and, later, capture).</summary>
    public nint PreviousForeground { get; private set; }

    public PopupWindow(WindowSettings settings, int maxHeightDip, PopupToggleGuard guard, FileLog log, bool showDebugStatus)
    {
        _settings = settings;
        _maxHeightDip = maxHeightDip;
        _guard = guard;
        _log = log;
        InitializeComponent();
        Web.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
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

        Activated += OnActivated;
    }

    public bool IsShown => AppWindow.IsVisible;

    public void ShowPopup()
    {
        var fg = Native.GetForegroundWindow();
        if (fg != Hwnd && fg != 0)
            PreviousForeground = fg;

        PlaceOnActiveMonitor();
        Activate();
        Native.SetForegroundWindow(Hwnd);
        Web.Focus(FocusState.Programmatic);
        Shown?.Invoke();
    }

    public void HidePopup()
    {
        if (!AppWindow.IsVisible) return;
        AppWindow.Hide();
        _guard.NoteHidden();
    }

    public void Toggle()
    {
        if (_guard.ShouldShowOnToggle(AppWindow.IsVisible)) ShowPopup();
        else HidePopup();
    }

    public void SetStatus(string text) => StatusText.Text = text;

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated && _settings.HideOnBlur && _modal == 0)
            HidePopup();
    }

    private void PlaceOnActiveMonitor()
    {
        var anchor = PreviousForeground != 0 ? PreviousForeground : Hwnd;
        // GetFromWindowId can return null for some shell/transient windows (crash seen on Copilot key taps);
        // fall back to the monitor under the cursor, then the primary monitor.
        var area = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(anchor), DisplayAreaFallback.Nearest);
        if (area is null)
        {
            _log.Error($"no display area for previous foreground window 0x{anchor:X} ({Native.ClassNameOf(anchor)}); using cursor monitor");
            Native.GetCursorPos(out var pt);
            area = DisplayArea.GetFromPoint(new PointInt32(pt.X, pt.Y), DisplayAreaFallback.Primary) ?? DisplayArea.Primary;
        }
        var wa = area.WorkArea;

        var monitor = Native.MonitorFromWindow(anchor, Native.MONITOR_DEFAULTTONEAREST);
        var scale = Native.GetDpiForMonitor(monitor, Native.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;

        var r = PopupGeometry.Place(new RectI(wa.X, wa.Y, wa.Width, wa.Height), _settings.Width, _settings.Height, scale, _settings.VerticalPosition);
        _log.Debug($"place: anchor=0x{anchor:X} ({Native.ClassNameOf(anchor)}) workArea={wa.X},{wa.Y} {wa.Width}x{wa.Height} scale={scale} -> {r}");
        (_bar, _work, _scale) = (r, new RectI(wa.X, wa.Y, wa.Width, wa.Height), scale);
        ApplyHeight();
    }

    public event Action? Shown;
    public event Action? NewChatRequested;
    public event Action<bool>? CaptureRequested;

    public void RequestNewChat() => NewChatRequested?.Invoke();
    public void RequestCapture(bool window) => CaptureRequested?.Invoke(window);

    /// <summary>Content height reported by the chat view (CSS px); the popup grows upward from the bar.</summary>
    public void SetContentHeight(double cssPx)
    {
        _contentPx = (int)Math.Ceiling(cssPx * _scale);
        if (AppWindow.IsVisible) ApplyHeight();
    }

    private void ApplyHeight()
    {
        if (_bar.Width == 0) return;
        var r = PopupGeometry.GrowUp(_bar, _contentPx, (int)Math.Round(_maxHeightDip * _scale), _work);
        AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
    }

    /// <summary>While disposed-not-yet, focus loss (file dialogs, captures) does not hide the popup.</summary>
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
            Web.Focus(FocusState.Programmatic);
        }
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
