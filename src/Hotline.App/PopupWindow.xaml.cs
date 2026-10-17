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
    public event Action? Hidden;
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
        var backdrop = Hotline.Core.Theming.ThemeTokens.EffectiveBackdrop(_settings.Backdrop, SystemTheme.Colors, _settings.Theme);
        SystemBackdrop = Backdrops.Create(_settings, backdrop);
        if (AppWindow.Presenter is OverlappedPresenter op) op.IsAlwaysOnTop = _settings.AlwaysOnTop;
        Root.RequestedTheme = SystemTheme.Resolve(_settings.Theme); // "System" follows Windows live
        if (backdrop != BackdropKind.Solid) Root.Background = null;
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

    /// <param name="readSelection">False when the caller brings the content itself (an App Action).</param>
    public void ShowPopup(bool readSelection = true)
    {
        var fg = Native.GetForegroundWindow();
        if (fg != Hwnd && fg != 0 && !ShellSurfaces.IsShell(Native.ClassNameOf(fg)))
        {
            PreviousForeground = fg; // keep the last real app window; taskbar/desktop/flyouts don't count
            if (readSelection)
            {
                try { BeforeShow?.Invoke(fg); } // e.g. read the selection there before we take focus (also when pinned open)
                catch (Exception ex) { _log.Error("before-show step failed", ex); }
            }
        }

        if (_offscreen)
        {
            // A self-test that never finished (it once hung in its OCR step) must not keep the panel parked off-screen:
            // every later open "showed" it at -32000 and nothing appeared.
            _log.Error("opening while the off-screen self-test still held the panel; ending it");
            _offscreen = false;
        }
        PlaceOnActiveMonitor();
        _shownAtMs = Environment.TickCount64;
        _reclaimed = 0;
        _shows++;
        Activate();
        if (!Native.ForceForeground(Hwnd)) _log.Error("could not take foreground; typing goes elsewhere until the panel is clicked");
        Shown?.Invoke();
        CheckShown();
    }

    private int _showCheck;

    /// <summary>
    /// After waking from Modern Standby the panel once toggled "open" without appearing (2026-10-16). Half a second
    /// after each open: if Windows says it's hidden or cloaked, or no frame was drawn, log what was seen and show it
    /// once more.
    /// </summary>
    private void CheckShown()
    {
        var check = ++_showCheck;
        var drawn = false;
        void OnFrame(object? s, object e) { drawn = true; Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFrame; }
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFrame;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFrame;
            if (check != _showCheck || !AppWindow.IsVisible || _offscreen) return; // closed or reopened since
            var visible = Native.IsWindowVisible(Hwnd);
            var cloaked = Native.DwmGetWindowAttributeInt(Hwnd, Native.DWMWA_CLOAKED, out var c, sizeof(int)) == 0 && c != 0;
            var p = AppWindow.Position;
            var state = $"visible={visible} cloaked={cloaked} drawn={drawn} at {p.X},{p.Y} {AppWindow.Size.Width}x{AppWindow.Size.Height}";
            if (cloaked) _log.Info($"panel open but cloaked ({state})"); // e.g. another virtual desktop: leave it
            if (visible && drawn) return;
            _log.Error($"panel opened but isn't on screen ({state}); showing it again");
            var modal = Modal(); // the brief hide isn't the user clicking away
            AppWindow.Hide();
            AppWindow.Show(activateWindow: true);
            Native.ForceForeground(Hwnd);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, modal.Dispose);
        };
        timer.Start();
    }

    private bool _offscreen;
    private long _shownAtMs;
    private int _reclaimed;
    private int _shows;
    private long _consoleReclaimLaunch = long.MinValue;

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
        Hidden?.Invoke();
    }

    /// <summary>Hides for a system dialog (file picker) without counting as a user dismissal.</summary>
    public void HideForDialog() => AppWindow.Hide();

    public void Toggle()
    {
        // Evidence for the sound some hear when the key toggles a pinned panel: what had focus at the time.
        var fg = Native.GetForegroundWindow();
        _log.Info($"toggle: visible={AppWindow.IsVisible} pinned={Pinned} foreground={(fg == Hwnd ? "panel" : Native.ClassNameOf(fg))}");
        if (_guard.ShouldShowOnToggle(AppWindow.IsVisible)) ShowPopup();
        else HidePopup();
    }

    public void SetStatus(string text) => StatusText.Text = text;

    public void RequestNewChat() => NewChatRequested?.Invoke();
    public void RequestCapture(bool window) => CaptureRequested?.Invoke(window);
    public event Action? RegionCaptureRequested;
    public void RequestRegionCapture() => RegionCaptureRequested?.Invoke();

    /// <summary>Content height (DIPs) of the conversation; the panel grows upward from its baseline.</summary>
    /// <param name="conversation">The chat has messages: the panel then expands to halfway at once, and past it with the text.</param>
    public void SetContentHeight(double dip, bool conversation)
    {
        _contentDip = dip; // DIPs: converted with the current monitor scale in ApplyHeight
        _conversation = conversation;
        if (AppWindow.IsVisible) ApplyHeight();
    }

    /// <summary>While held, focus loss (dialogs, captures) does not hide the popup.</summary>
    public IDisposable Modal()
    {
        _modal++;
        return new Releaser(() => _modal--);
    }

    /// <summary>
    /// While held, the window keeps its size: resizing a window closes its open flyouts at once (the history list
    /// opened from the keyboard did exactly that). A resize asked for meanwhile is applied when the last hold ends.
    /// Only flyouts take it; dialogs and captures still resize as usual.
    /// </summary>
    public IDisposable HoldResize()
    {
        _resizeHolds++;
        return new Releaser(() =>
        {
            void Release()
            {
                _resizeHolds--;
                if (_resizeHolds == 0 && _resizeHeld && AppWindow.IsVisible) ApplyHeight();
            }
            if (DispatcherQueue.HasThreadAccess) Release(); else DispatcherQueue.TryEnqueue(Release);
        });
    }

    private int _resizeHolds;
    /// <summary>A resize was asked for during a HoldResize; any real resize settles it.</summary>
    private bool _resizeHeld;

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
        // after we appear. That isn't the user clicking away: take focus back instead of hiding (once per show; up to
        // three times on the first show after launch, when focus can bounce more than once).
        if (Environment.TickCount64 - _shownAtMs < 1500 && _reclaimed < (_shows <= 1 ? 3 : 1))
        {
            _reclaimed++;
            _log.Info("lost focus right after opening; taking it back");
            DispatcherQueue.TryEnqueue(() => { if (AppWindow.IsVisible && !Native.ForceForeground(Hwnd)) _log.Error("could not take focus back"); });
            return;
        }
        ConsiderBlur(retried: false);
    }

    /// <summary>Whether a process is one Hotline started (a CLI, or something it started); set by App.</summary>
    public Func<uint, bool>? IsOurProcess { get; set; }

    /// <summary>
    /// Focus went elsewhere: hide, unless the window that took it belongs to a program Hotline started (a CLI opening a
    /// console on its first run). That isn't the user clicking away: keep the panel and take focus back.
    /// </summary>
    private void ConsiderBlur(bool retried)
    {
        if (!AppWindow.IsVisible || !_settings.HideOnBlur || _modal != 0 || Pinned) return; // may have changed during the retry
        var fg = Native.GetForegroundWindow();
        if (fg == Hwnd) return; // it came back
        if (fg == 0 && !retried)
        {
            // Mid-switch there may be no foreground window yet: look again in a moment.
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(150);
            timer.IsRepeating = false;
            timer.Tick += (_, _) => ConsiderBlur(retried: true);
            timer.Start();
            return;
        }
        if (fg != 0 && IsOurConsole(fg))
        {
            _consoleReclaimLaunch = Hotline.Core.Processes.ProcessLaunches.Last; // once per launch: never a tug of war
            _log.Info($"a window from a program Hotline started ({Native.ClassNameOf(fg)}) took focus; keeping the panel");
            if (!Native.ForceForeground(Hwnd)) _log.Error("could not take focus back");
            return;
        }
        HidePopup();
    }

    /// <summary>
    /// Only console/terminal windows count (never a browser a CLI opens to sign in), at most once per program launch:
    /// a plain console must belong to a process Hotline started (its job); a Windows Terminal window (owned by
    /// WindowsTerminal.exe, so the job can't tell) must appear within 2 s of a launch.
    /// </summary>
    private bool IsOurConsole(nint window)
    {
        if (Hotline.Core.Processes.ProcessLaunches.Last == _consoleReclaimLaunch) return false;
        var cls = Native.ClassNameOf(window);
        if (cls is "ConsoleWindowClass" or "PseudoConsoleWindow")
        {
            Native.GetWindowThreadProcessId(window, out var pid);
            return IsOurProcess?.Invoke(pid) == true || Hotline.Core.Processes.ProcessLaunches.Recent(TimeSpan.FromSeconds(2));
        }
        return cls == "CASCADIA_HOSTING_WINDOW_CLASS" && Hotline.Core.Processes.ProcessLaunches.Recent(TimeSpan.FromSeconds(2));
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

    private RectI _targetRect;
    private RectI? _currentRect;
    private bool _conversation;

    /// <summary>
    /// Sizes the panel to its content, bottom edge fixed. In the usual "fit" mode it expands to halfway after the first
    /// message and then only as far as the text needs (see PopupGeometry.TwoStep), sliding there. Resizing a window
    /// upward is never perfectly smooth (each resize shows the old frame at the new top edge for a moment), so a short
    /// answer never resizes it after that first expansion.
    /// </summary>
    private void ApplyHeight(bool force = false)
    {
        if (_offscreen) return; // never move the off-screen self-test window onto a monitor
        if (_resizeHolds > 0 && !force) { _resizeHeld = true; return; } // a flyout is open: see HoldResize
        if (_bar.Width == 0) return;
        _resizeHeld = false; // this resize settles any held one
        var maxPx = Math.Min((int)Math.Round(_work.Height * _settings.MaxHeightPercent / 100.0), _work.Height);
        // Before the first layout pass there is no measured content yet: keep a sane minimum instead of 0 px.
        var contentPx = (int)Math.Ceiling(Math.Max(_contentDip, 56) * _scale);
        if (GrowMode == GrowMode.Grow)
        {
            var previous = _targetRect.Height > 0 ? _targetRect.Height : _bar.Height;
            contentPx = PopupGeometry.TwoStep(previous, contentPx, _bar.Height, Math.Max(_bar.Height, maxPx), _conversation);
        }
        _targetRect = PopupGeometry.GrowUp(_bar, contentPx, maxPx, _work, GrowMode);
        // During a chat the panel slides to its new height; anything else (no message yet, a new chat, placing) moves at once.
        if (!force && _conversation && GrowMode == GrowMode.Grow && AppWindow.IsVisible && _currentRect is { } current
            && current.Width == _targetRect.Width && current.Y + current.Height == _targetRect.Y + _targetRect.Height)
        {
            if (current == _targetRect || _animating) return; // already there, or the running slide retargets next frame
            _animating = true;
            _animatedHeight = current.Height;
            _lastFrame = System.Diagnostics.Stopwatch.GetTimestamp();
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFrame;
            return;
        }
        StopAnimation();
        if (force || _currentRect != _targetRect) MoveTo(_targetRect); // force: placing it anew (the window may have been moved)
    }

    private bool _animating;
    private double _animatedHeight;
    private long _lastFrame;

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFrame;
    }

    /// <summary>
    /// One display frame of a slide: exponential ease toward the target height (time constant ~70 ms, frame-rate
    /// independent), bottom edge fixed. Runs on the compositor's frame clock so steps line up with screen refreshes.
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
        ApplyHeight(force: true);
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
