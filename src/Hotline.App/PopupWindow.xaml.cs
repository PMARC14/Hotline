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
    public GrowMode GrowMode { get; set; }
    private readonly PopupToggleGuard _guard;
    private readonly FileLog _log;
    private int _modal;
    private RectI _bar;
    private RectI _work;
    private double _scale = 1;
    private double _contentDip;

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
        GrowMode = growMode;
        _guard = guard;
        _log = log;
        InitializeComponent();
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        ExtendsContentIntoTitleBar = true;

        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false; // Hotline sizes the panel (settings: width %, heights); dragging the edge would fight it
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon("Assets\\Hotline.ico");
        AppWindow.Closing += (_, e) => { e.Cancel = true; HidePopup(); };

        if (showDebugStatus)
            StatusText.Visibility = Visibility.Visible;
        ApplyAppearance();

        Activated += OnActivated;
        _scrollbarHide = DispatcherQueue.CreateTimer();
        _scrollbarHide.Interval = TimeSpan.FromMilliseconds(1200);
        _scrollbarHide.Tick += (_, _) =>
        {
            if (_pointerOnScrollbar) return; // keep it while hovered; re-checked next tick
            _scrollbarHide.Stop();
            if (_settings.Scrollbar == ScrollbarStyle.Auto)
                MessagesScroll.VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden;
        };
        MessagesScroll.PointerWheelChanged += (_, _) => ShowScrollbarBriefly();
        MessagesScroll.PointerMoved += (_, e) =>
        {
            _pointerOnScrollbar = e.GetCurrentPoint(MessagesScroll).Position.X > MessagesScroll.ActualWidth - 20;
            if (_pointerOnScrollbar) ShowScrollbarBriefly();
        };
        MessagesScroll.PointerExited += (_, _) => _pointerOnScrollbar = false;
    }

    /// <summary>Re-applies backdrop, theme, always-on-top and scrollbar from the (live) settings object.</summary>
    public void ApplyAppearance()
    {
        SystemBackdrop = Backdrops.Create(_settings);
        if (AppWindow.Presenter is OverlappedPresenter op) op.IsAlwaysOnTop = _settings.AlwaysOnTop;
        Root.RequestedTheme = _settings.Theme switch
        {
            ThemeChoice.Light => ElementTheme.Light,
            ThemeChoice.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        if (_settings.Backdrop != BackdropKind.Solid) Root.Background = null;
        MessagesScroll.VerticalScrollBarVisibility = _settings.Scrollbar switch
        {
            ScrollbarStyle.Visible => Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Visible,
            ScrollbarStyle.Hidden => Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden,
            _ => Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden, // Auto: shown on wheel/hover (ShowScrollbarBriefly)
        };
        if (AppWindow.IsVisible) PlaceOnActiveMonitor();
    }

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _scrollbarHide;
    private bool _pointerOnScrollbar;

    /// <summary>Scrollbar style Auto: invisible until the user scrolls or points at the right edge, then fades.</summary>
    private void ShowScrollbarBriefly()
    {
        if (_settings.Scrollbar != ScrollbarStyle.Auto) return;
        MessagesScroll.VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto;
        _scrollbarHide.Stop();
        _scrollbarHide.Start();
    }

    /// <summary>The bar's items in order (from toolbar.json); the provider bar reads which pickers are shown.</summary>
    public IReadOnlyList<string> ToolbarItems { get; private set; } = Hotline.Core.Windowing.ToolbarConfig.Defaults;

    /// <summary>Rebuilds the bottom bar: one column per item in order, a flexible column for each "spacer".</summary>
    public void ApplyToolbar(IReadOnlyList<string> items)
    {
        ToolbarItems = items;
        var elements = new Dictionary<string, FrameworkElement>
        {
            ["pin"] = PinButton, ["captureWindow"] = CaptureWindowButton, ["captureScreen"] = CaptureScreenButton,
            ["captureRegion"] = CaptureRegionButton, ["prompt"] = PromptButton, ["recent"] = RecentButton,
            ["newChat"] = NewChatButton, ["settings"] = SettingsButton,
        };
        Toolbar.Children.Clear();
        Toolbar.ColumnDefinitions.Clear();
        var pickersPlaced = false;
        foreach (var item in items)
        {
            FrameworkElement? element;
            var star = false;
            if (item == "spacer") { element = new Microsoft.UI.Xaml.Controls.Border { Tag = "spacer" }; star = true; }
            else if (item is "effort" or "model" or "provider")
            {
                if (pickersPlaced) continue; // the pickers sit together, in the order listed
                pickersPlaced = true;
                element = PickersPanel;
                var order = items.Where(i => i is "effort" or "model" or "provider").ToList();
                var boxes = new Dictionary<string, Microsoft.UI.Xaml.Controls.ComboBox> { ["effort"] = EffortBox, ["model"] = ModelBox, ["provider"] = ProviderBox };
                PickersPanel.Children.Clear();
                foreach (var name in order) PickersPanel.Children.Add(boxes[name]);
            }
            else if (!elements.TryGetValue(item, out element)) continue;
            Toolbar.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = star ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(element, Toolbar.ColumnDefinitions.Count - 1);
            element.Visibility = Visibility.Visible;
            Toolbar.Children.Add(element);
        }
        ToolbarChanged?.Invoke();
    }

    public event Action? ToolbarChanged;

    public bool IsShown => AppWindow.IsVisible;

    /// <summary>Runs synchronously when the panel is about to open over a real app window (its handle), before focus moves.</summary>
    public Action<nint>? BeforeShow { get; set; }

    public void ShowPopup()
    {
        var fg = Native.GetForegroundWindow();
        if (fg != Hwnd && fg != 0 && !ShellSurfaces.IsShell(Native.ClassNameOf(fg)))
        {
            PreviousForeground = fg; // keep the last real app window; taskbar/desktop/flyouts don't count
            if (!AppWindow.IsVisible)
            {
                try { BeforeShow?.Invoke(fg); } // e.g. read the selection there before we take focus
                catch (Exception ex) { _log.Error("before-show step failed", ex); }
            }
        }

        PlaceOnActiveMonitor();
        _shownAtMs = Environment.TickCount64;
        _reclaimed = false;
        Activate();
        if (!Native.ForceForeground(Hwnd)) _log.Error("could not take foreground; typing goes elsewhere until the panel is clicked");
        Shown?.Invoke();
    }

    private bool _offscreen;
    private long _shownAtMs;
    private bool _reclaimed;

    /// <summary>Self-test only: shows the panel far outside every monitor, without activation or focus, so the
    /// conversation is really drawn (composition) without the user seeing anything.</summary>
    public void ShowOffscreen()
    {
        _offscreen = true;
        AppWindow.MoveAndResize(new RectInt32(-32000, -32000, 700, 700));
        AppWindow.Show(activateWindow: false);
    }

    public void EndOffscreen()
    {
        AppWindow.Hide();
        _offscreen = false;
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
    public event Action? RegionCaptureRequested;
    public void RequestRegionCapture() => RegionCaptureRequested?.Invoke();

    /// <summary>Content height (DIPs) of the conversation; the panel grows upward from its baseline.</summary>
    public void SetContentHeight(double dip)
    {
        _contentDip = dip; // DIPs: converted with the current monitor scale in ApplyHeight
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
            // ForceForeground: a plain SetForegroundWindow can be refused once another window (e.g. the region
            // picker) has just closed and Windows handed focus to another app; then hide-on-blur would hide us.
            if (!Native.ForceForeground(Hwnd)) _log.Error("could not take focus back after hidden work");
            Shown?.Invoke();
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated || !_settings.HideOnBlur || _modal != 0 || Pinned) return;
        // On a cold start (Hotline launched by the key) Windows can hand focus back to the previous window right
        // after we appear. That isn't the user clicking away: take focus back instead of hiding (once per show).
        if (Environment.TickCount64 - _shownAtMs < 1500 && !_reclaimed)
        {
            _reclaimed = true;
            _log.Info("lost focus right after opening; taking it back");
            DispatcherQueue.TryEnqueue(() => { if (AppWindow.IsVisible && !Native.ForceForeground(Hwnd)) _log.Error("could not take focus back"); });
            return;
        }
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

    private bool _animating;
    private double _animatedHeight;
    private long _lastFrame;
    private RectI _targetRect;
    private RectI? _currentRect;

    /// <summary>
    /// Sizes the panel to its content. While visible, the height eases toward the new size over a few frames
    /// (bottom edge fixed) instead of jumping, so streamed answers and new messages slide in smoothly.
    /// </summary>
    private void ApplyHeight(bool animate = true)
    {
        if (_offscreen) return; // never move the off-screen self-test window onto a monitor
        if (_bar.Width == 0) return;
        var maxPx = (int)Math.Round(_work.Height * _settings.MaxHeightPercent / 100.0);
        // Before the first layout pass there is no measured content yet: keep a sane minimum instead of 0 px.
        var contentPx = (int)Math.Ceiling(Math.Max(_contentDip, 56) * _scale);
        _targetRect = PopupGeometry.GrowUp(_bar, contentPx, maxPx, _work, GrowMode);
        if (!animate || _currentRect is not { } current || !AppWindow.IsVisible || current.Width != _targetRect.Width
            || current.Y + current.Height != _targetRect.Y + _targetRect.Height)
        {
            StopAnimation();
            MoveTo(_targetRect);
            return;
        }
        if (_animating) return; // the running animation retargets on its next frame
        _animating = true;
        _animatedHeight = current.Height;
        _lastFrame = System.Diagnostics.Stopwatch.GetTimestamp();
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFrame;
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFrame;
    }

    /// <summary>
    /// One display frame: exponential ease toward the target height (time constant ~70 ms, frame-rate independent),
    /// bottom edge fixed. Runs on the compositor's frame clock so steps line up with screen refreshes.
    /// </summary>
    private void OnFrame(object? sender, object e)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var dt = Math.Clamp((now - _lastFrame) / (double)System.Diagnostics.Stopwatch.Frequency, 0.001, 0.1);
        _lastFrame = now;
        if (!AppWindow.IsVisible) { StopAnimation(); return; }
        var target = _targetRect.Height;
        _animatedHeight += (target - _animatedHeight) * (1 - Math.Exp(-dt / 0.07));
        if (Math.Abs(target - _animatedHeight) < 0.75)
        {
            StopAnimation();
            MoveTo(_targetRect);
            return;
        }
        var height = (int)Math.Round(_animatedHeight);
        if (_currentRect is { } current && current.Height == height) return; // sub-pixel step: nothing to move yet
        MoveTo(_targetRect with { Y = _targetRect.Y + _targetRect.Height - height, Height = height });
    }

    private void MoveTo(RectI r)
    {
        _currentRect = r;
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
        ApplyHeight(animate: false);
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
