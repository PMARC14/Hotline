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

    /// <summary>The top row's items (toolbar.json "top").</summary>
    public IReadOnlyList<string> TopToolbarItems { get; private set; } = [];

    /// <summary>
    /// Rebuilds both button rows: one column per item in order, a flexible column for each "spacer". The pickers sit
    /// together in the row of the first one listed.
    /// </summary>
    public void ApplyToolbar(IReadOnlyList<string> items, IReadOnlyList<string> top)
    {
        (ToolbarItems, TopToolbarItems) = (items, top);
        var elements = new Dictionary<string, FrameworkElement>
        {
            ["pin"] = PinButton, ["captureWindow"] = CaptureWindowButton, ["captureScreen"] = CaptureScreenButton,
            ["captureRegion"] = CaptureRegionButton, ["prompt"] = PromptButton, ["recent"] = RecentButton, ["continueIn"] = ContinueInButton,
            ["newChat"] = NewChatButton, ["settings"] = SettingsButton,
        };
        foreach (var row in new[] { Toolbar, TopToolbar }) { row.Children.Clear(); row.ColumnDefinitions.Clear(); }
        foreach (var e in elements.Values.Append(PickersPanel)) e.Visibility = Visibility.Collapsed; // shown again where listed
        var pickersPlaced = false;
        var all = top.Concat(items).ToList();
        void Fill(Microsoft.UI.Xaml.Controls.Grid row, IReadOnlyList<string> list)
        {
            foreach (var item in list)
            {
                FrameworkElement? element;
                var star = false;
                if (item == "spacer") { element = new Microsoft.UI.Xaml.Controls.Border { Tag = "spacer" }; star = true; }
                else if (item is "effort" or "model" or "provider")
                {
                    if (pickersPlaced) continue; // the pickers sit together, in the order listed
                    pickersPlaced = true;
                    element = PickersPanel;
                    var order = all.Where(i => i is "effort" or "model" or "provider").ToList();
                    var boxes = new Dictionary<string, Microsoft.UI.Xaml.Controls.ComboBox> { ["effort"] = EffortBox, ["model"] = ModelBox, ["provider"] = ProviderBox };
                    PickersPanel.Children.Clear();
                    foreach (var name in order) PickersPanel.Children.Add(boxes[name]);
                }
                else if (!elements.TryGetValue(item, out element)) continue;
                row.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = star ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
                Microsoft.UI.Xaml.Controls.Grid.SetColumn(element, row.ColumnDefinitions.Count - 1);
                element.Visibility = Visibility.Visible;
                row.Children.Add(element);
            }
        }
        Fill(TopToolbar, top);
        Fill(Toolbar, items);
        TopToolbar.Visibility = TopToolbar.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Toolbar.Visibility = Toolbar.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        if (!_offscreen) return; // a real open already ended it: never hide the panel the user is looking at
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
        var focused = Content?.XamlRoot is { } root ? Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) : null;
        var focusName = focused is FrameworkElement { Name.Length: > 0 } named ? named.Name : focused?.GetType().Name ?? "none";
        var previous = PreviousForeground == 0 ? "none" : Native.ClassNameOf(PreviousForeground);
        var foreground = fg == Hwnd ? "panel" : Native.ClassNameOf(fg);
        _log.Info($"toggle: visible={AppWindow.IsVisible} pinned={Pinned} foreground={foreground} focus={focusName} previous={previous}");
        if (_guard.ShouldShowOnToggle(AppWindow.IsVisible)) ShowPopup();
        else
        {
            HidePopup();
            // Closed by the key while it had focus (a pinned panel keeps it; unpinned, the shell usually took it first):
            // no window had focus afterwards, the rest of the key press went nowhere and Windows played its warning
            // sound. Give focus back to the app the panel opened over.
            if (fg == Hwnd && PreviousForeground != 0 && Native.IsWindow(PreviousForeground))
            {
                if (Native.ForceForeground(PreviousForeground)) _log.Info($"focus back to {Native.ClassNameOf(PreviousForeground)}");
                else _log.Info("could not give focus back to the previous window");
            }
        }
    }

    public void SetStatus(string text) => StatusText.Text = text;

    public void RequestNewChat() => NewChatRequested?.Invoke();
    public void RequestCapture(bool window) => CaptureRequested?.Invoke(window);
    public event Action? RegionCaptureRequested;
    public void RequestRegionCapture() => RegionCaptureRequested?.Invoke();

    /// <summary>Content height (DIPs) of the conversation; the panel grows upward from its baseline.</summary>
    /// <param name="conversation">The chat has messages: the panel then expands to halfway at once, and past it with the text.</param>
    /// <param name="barDip">The bar alone (message box and button rows): kept in place when the chat grows down.</param>
    public void SetContentHeight(double dip, bool conversation, double barDip)
    {
        _contentDip = dip; // DIPs: converted with the current monitor scale in ApplyHeight
        _conversation = conversation;
        _barDip = barDip;
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
        if (e.WindowActivationState == WindowActivationState.Deactivated && Pinned && _modal == 0 && _session is not null)
            DispatcherQueue.TryEnqueue(() =>
            {
                // Clicking into another app while pinned forgets the moved spot (owner, 2026-10-17); the shell (the
                // key, the taskbar) and programs Hotline started don't count.
                var fg = Native.GetForegroundWindow();
                if (fg == 0 || fg == Hwnd || Hotline.Core.Windowing.ShellSurfaces.IsShell(Native.ClassNameOf(fg))) return;
                Native.GetWindowThreadProcessId(fg, out var pid);
                if (pid == Environment.ProcessId || IsOurProcess?.Invoke(pid) == true) return;
                ForgetSessionSpot();
            });
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
        var maxPx = MaxPx;
        var bar = _growUp ? _bar : DownBar;
        // Before the first layout pass there is no measured content yet: keep a sane minimum instead of 0 px.
        var contentPx = (int)Math.Ceiling(Math.Max(_contentDip, 56) * _scale);
        if (GrowMode == GrowMode.Grow)
        {
            var previous = _targetRect.Height > 0 ? _targetRect.Height : bar.Height;
            contentPx = PopupGeometry.TwoStep(previous, contentPx, bar.Height, Math.Max(bar.Height, maxPx), _conversation);
        }
        _targetRect = _growUp ? PopupGeometry.GrowUp(bar, contentPx, maxPx, _work, GrowMode) : PopupGeometry.GrowDown(bar, contentPx, maxPx, _work, GrowMode);
        // During a chat the panel slides to its new height; anything else (no message yet, a new chat, placing) moves at once.
        // The bar's edge stays put: the bottom when growing up, the top when growing down.
        if (!force && _conversation && GrowMode == GrowMode.Grow && AppWindow.IsVisible && _currentRect is { } current
            && current.Width == _targetRect.Width
            && (_growUp ? current.Y + current.Height == _targetRect.Y + _targetRect.Height : current.Y == _targetRect.Y))
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
        MoveTo(_growUp ? _targetRect with { Y = _targetRect.Y + _targetRect.Height - height, Height = height } : _targetRect with { Height = height });
    }

    private void MoveTo(RectI r)
    {
        _currentRect = r;
        AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
    }

    private double _barDip;
    private bool _growUp = true;

    /// <summary>The bar alone, in pixels (measured; a sane minimum before the first layout).</summary>
    private int BarPx => Math.Max(_bar.Height, (int)Math.Ceiling(Math.Max(_barDip, 56) * _scale));

    /// <summary>
    /// The bar's rectangle: its bottom edge is the placement line in both directions, kept inside the work area (a line
    /// near the top has no room for the bar above it).
    /// </summary>
    private RectI DownBar => Hotline.Core.Windowing.PanelPlacement.Clamp(new RectI(_bar.X, _bar.Y + _bar.Height - BarPx, _bar.Width, BarPx), _work);

    /// <summary>The tallest the panel may be: the bar plus window.chatHeight, else maxHeightPercent of the work area.</summary>
    private int MaxPx => Math.Min(_work.Height, _settings.ChatHeight is { } chat
        ? BarPx + (int)Math.Round(chat * _scale)
        : (int)Math.Round(_work.Height * _settings.MaxHeightPercent / 100.0));

    /// <summary>While pinned, the spot the panel was moved/resized to this session (never saved; see ForgetSessionSpot).</summary>
    private sealed record SessionSpot(RectI Bar, RectI Work, double Scale);
    private SessionSpot? _session;

    /// <summary>The panel goes back to its saved default on the next open (unpinned, clicked into another app).</summary>
    public void ForgetSessionSpot()
    {
        if (_session is null) return;
        _session = null;
        _log.Info("pinned spot forgotten: the next open uses the default");
    }

    /// <summary>Picks the chat's direction for the current bar and lays the panel out for it.</summary>
    private void ChooseDirection()
    {
        var chatPx = Math.Max(0, MaxPx - BarPx);
        var up = Hotline.Core.Windowing.PanelPlacement.GrowsUp(_settings.GrowDirection, DownBar, _work, chatPx);
        if (up == _growUp) return;
        _growUp = up;
        DirectionChanged?.Invoke(up);
    }

    /// <summary>The chat now grows up (true) or down: the presenter swaps the layout.</summary>
    public event Action<bool>? DirectionChanged;
    public bool GrowsUp => _growUp;

    private void PlaceOnActiveMonitor()
    {
        if (Pinned && _session is { } spot && MonitorAt(spot.Work) is { } still)
        {
            // Pinned and moved this session: the same spot (pulled inside its monitor again, in case its work area changed).
            (_work, _scale) = (still.Work, still.Scale);
            _bar = Hotline.Core.Windowing.PanelPlacement.Clamp(Hotline.Core.Windowing.PanelPlacement.Rescale(spot.Bar, spot.Scale, still.Scale), still.Work);
            ChooseDirection();
            _log.Info($"place: pinned spot {_bar} on {still.Device} scale={still.Scale} up={_growUp}");
            ApplyHeight(force: true);
            return;
        }
        var (area, monitor, how) = HomeMonitor();
        var wa = area.WorkArea;
        var scale = Native.GetDpiForMonitor(monitor, Native.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        var work = new RectI(wa.X, wa.Y, wa.Width, wa.Height);
        var widthDip = _settings.Width ?? PopupGeometry.RelativeWidthDip(wa.Width, scale, _settings.WidthPercent, _settings.MinWidth, _settings.MaxWidth);
        var r = Hotline.Core.Windowing.PanelPlacement.Home(work, widthDip, _settings.Height, scale, _settings.Anchor);
        (_bar, _work, _scale) = (r, work, scale);
        ChooseDirection();
        _log.Info($"place: {how} {Native.MonitorDevice(monitor)} work={wa.X},{wa.Y} {wa.Width}x{wa.Height} scale={scale} anchor={_settings.Anchor.X:0.###},{_settings.Anchor.Y:0.###} -> {r} up={_growUp}");
        ApplyHeight(force: true);
    }

    /// <summary>The monitor for the saved default: the one you're working on, the primary one, or a named one.</summary>
    private (DisplayArea Area, nint Monitor, string How) HomeMonitor()
    {
        var wanted = _settings.Anchor.Monitor;
        if (string.Equals(wanted, Hotline.Core.Settings.PanelAnchor.Primary, StringComparison.OrdinalIgnoreCase))
        {
            var primary = DisplayArea.Primary;
            return (primary, Win32Interop.GetMonitorFromDisplayId(primary.DisplayId), "primary");
        }
        if (!string.Equals(wanted, Hotline.Core.Settings.PanelAnchor.Active, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var a in DisplayArea.FindAll())
            {
                var m = Win32Interop.GetMonitorFromDisplayId(a.DisplayId);
                if (string.Equals(Native.MonitorDevice(m), wanted, StringComparison.OrdinalIgnoreCase)) return (a, m, "saved monitor");
            }
            _log.Info($"saved monitor {wanted} isn't connected; using the active one");
        }
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
        return (area, monitor, "active");
    }

    /// <summary>The monitor that still holds a work area (null when it was unplugged): its current work area and scale.</summary>
    private static (RectI Work, double Scale, string? Device)? MonitorAt(RectI work)
    {
        var rect = new Native.RECT { Left = work.X, Top = work.Y, Right = work.X + work.Width, Bottom = work.Y + work.Height };
        var monitor = Native.MonitorFromRect(ref rect, Native.MONITOR_DEFAULTTONULL);
        if (monitor == 0) return null;
        var area = DisplayArea.GetFromDisplayId(Win32Interop.GetDisplayIdFromMonitor(monitor));
        if (area is null) return null;
        var wa = area.WorkArea;
        var scale = Native.GetDpiForMonitor(monitor, Native.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        return (new RectI(wa.X, wa.Y, wa.Width, wa.Height), scale, Native.MonitorDevice(monitor));
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
