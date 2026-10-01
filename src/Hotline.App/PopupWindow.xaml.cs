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

    public nint Hwnd { get; }
    /// <summary>The window the user was in before the popup appeared (target for monitor choice and, later, capture).</summary>
    public nint PreviousForeground { get; private set; }

    public PopupWindow(WindowSettings settings, PopupToggleGuard guard, FileLog log, bool showDebugStatus)
    {
        _settings = settings;
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
        PromptBox.Focus(FocusState.Programmatic);
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

    public void ResetConversation() => PromptBox.Text = string.Empty;

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated && _settings.HideOnBlur)
            HidePopup();
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            HidePopup();
            e.Handled = true;
        }
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
        AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
    }
}
