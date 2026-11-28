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
public sealed partial class PopupWindow : Window, IPlacementSource
{
    private readonly WindowSettings _settings;
    public GrowMode GrowMode { get => _geometry.GrowMode; set => _geometry.GrowMode = value; }
    private readonly PopupToggleGuard _guard;
    private readonly FileLog _log;
    private readonly ForegroundWindowTracker _foreground;
    private readonly PanelDisplayObserver _displayObserver;
    private int _modal;
    /// <summary>Placement, size, the session spot, drags and display changes (Core; see <see cref="PanelGeometry"/>).</summary>
    private readonly PanelGeometry _geometry;

    public nint Hwnd { get; }
    /// <summary>The window the user was in before the popup appeared (target for monitor choice and capture).</summary>
    public nint PreviousForeground { get; private set; }
    /// <summary>Pinned: stays open when it loses focus (so files can be dragged in from Explorer).</summary>
    public bool Pinned { get; set; }

    public event Action? Shown;
    public event Action? Hidden;
    public event Action? NewChatRequested;
    public event Action<bool>? CaptureRequested;

    public PopupWindow(WindowSettings settings, GrowMode growMode, PopupToggleGuard guard, FileLog log, bool showDebugStatus,
        ForegroundWindowTracker? foreground = null, Hotline.Core.State.StateStore? state = null)
    {
        _settings = settings;
        _guard = guard;
        _log = log;
        _foreground = foreground ?? new ForegroundWindowTracker(log);
        InitializeComponent();
        Input.TextCompositionStarted += (_, _) => IsComposing = true;
        Input.TextCompositionEnded += (_, _) => IsComposing = false;
        // The grips reach the window's edge: the negated padding of Root (one source of truth, XAML has no way to bind it).
        var pad = Root.Padding;
        GripLayer.Margin = new Thickness(-pad.Left, -pad.Top, -pad.Right, -pad.Bottom);
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _geometry = new PanelGeometry(settings, growMode, new PanelWindowAdapter(this), new Windowing.WinMonitors(), this, log, state);
        _rows = new ToolbarRenderer(this, b => CustomButtonClicked?.Invoke(b), log);

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
        _displayObserver = new PanelDisplayObserver(Root, DispatcherQueue, RecoverDisplay);
        Closed += (_, _) => _displayObserver.Dispose();

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
        _geometry.SettingsChanged(); // placed again when the layout settings changed (deferred while HoldResize is held), else sized
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

    private readonly ToolbarRenderer _rows;

    /// <summary>The button rows in force (toolbar.json, resolved); the provider bar reads which pickers are shown.</summary>
    public ToolbarModel ToolbarModel => _rows.Model;

    /// <summary>The theme's accent colour for the bar's outlines (see <see cref="ToolbarRenderer.SetAccent"/>).</summary>
    public void SetToolbarAccent(string? hex) => _rows.SetAccent(hex);

    /// <summary>The theme accent the bar's outlines use (#RRGGBB), null before a theme set one; Settings shows its swatch from it
    /// (see <see cref="ToolbarAccent"/>).</summary>
    public string? ToolbarAccentHex => ToolbarAccent.Theme;

    /// <summary>Rebuilds both button rows from the model (see <see cref="ToolbarRenderer"/>); the fit to the panel's width
    /// follows after layout (ProviderBar, through <see cref="ToolbarChanged"/>).</summary>
    public void ApplyToolbar(ToolbarModel model)
    {
        _rows.Apply(model);
        ToolbarChanged?.Invoke();
    }

    /// <summary>After layout only (ProviderBar.Layout): moves what doesn't fit into the "⋯" menus; returns each picker's width
    /// (0 = hidden), see <see cref="ToolbarPlan.PlanRow"/>.</summary>
    public IReadOnlyDictionary<string, double> FitToolbar(double room, IReadOnlyCollection<string> availablePickers) =>
        _rows.Fit(room, availablePickers);

    /// <summary>Self-test: shown buttons drawn at another width than fitted, and whether a row shrank its last right button.</summary>
    public (IReadOnlyList<string> Problems, bool AnyShrunk) ToolbarDrawnWidths() => _rows.DrawnWidths();

    /// <summary>Where a bar button's menu opens: the button, or "⋯" while the button is in that menu.</summary>
    public FrameworkElement ToolbarAnchor(FrameworkElement button) => _rows.Anchor(button);

    public void SetToolbarShortcuts(ChatSettings settings) => _rows.SetShortcuts(settings);
    public IReadOnlyDictionary<string, string> ToolbarShortcuts => _rows.Shortcuts;
    public string ShortcutTip(string id, string text) => _rows.ShortcutTip(id, text);
    public void InvokeToolbarShortcut(string id) => _rows.InvokeShortcut(id);

    /// <summary>A custom button (toolbar.json "buttons") or its "⋯" entry was clicked.</summary>
    public event Action<CustomButton>? CustomButtonClicked;

    public event Action? ToolbarChanged;

    public bool IsShown => AppWindow.IsVisible;

    /// <summary>Runs synchronously when the panel is about to open over a real app window (its handle), before focus moves.</summary>
    public Action<nint>? BeforeShow { get; set; }

    /// <summary>Revalidates the show-time target window right before a capture uses it.</summary>
    /// <summary>The display area and handle of a monitor that is still connected, else null (it was unplugged).</summary>
    public static (DisplayArea Area, nint Monitor)? ValidMonitor(nint monitor) =>
        monitor != 0 && Native.MonitorDevice(monitor) is not null
        && DisplayArea.GetFromDisplayId(Win32Interop.GetDisplayIdFromMonitor(monitor)) is { } area ? (area, monitor) : null;

    /// <summary>The monitor the user was on when the panel opened, when that was a click on a desktop or taskbar (the
    /// remembered app may be on another monitor); otherwise 0. Capture screen grabs this one.</summary>
    public nint UserMonitor { get; private set; }

    public nint RevalidateCaptureTarget(nint? candidate = null)
    {
        var fg = _foreground.RevalidateShowTarget(candidate ?? PreviousForeground);
        PreviousForeground = fg;
        return fg;
    }

    /// <param name="readSelection">False when the caller brings the content itself (an App Action).</param>
    public void ShowPopup(bool readSelection = true)
    {
        // The open sound plays only when it really opens (not back from the file picker).
        var hiddenForDialog = _hiddenForDialog;
        var wasShown = AppWindow.IsVisible && !_geometry.Offscreen || hiddenForDialog;
        _hiddenForDialog = false;
        var fg = hiddenForDialog ? _foreground.RevalidateShowTarget(PreviousForeground) : _foreground.ResolveForShow();
        PreviousForeground = fg;
        if (!wasShown) // a panel already open (or back from the file picker) keeps its context and its place
        {
            Native.GetCursorPos(out var cursor);
            UserMonitor = _foreground.PlaceBy == PlaceBy.Cursor ? Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST) : 0;
        }
        if (fg != 0 && !hiddenForDialog && readSelection)
        {
            try { BeforeShow?.Invoke(fg); } // e.g. read the selection there before we take focus (also when pinned open)
            catch (Exception ex) { _log.Error("before-show step failed", ex); }
        }

        if (_geometry.Offscreen)
        {
            // A self-test that never finished (it once hung in its OCR step) must not keep the panel parked off-screen:
            // every later open "showed" it at -32000 and nothing appeared.
            _log.Error("opening while the off-screen self-test still held the panel; ending it");
            _geometry.EndOffscreen();
        }
        // Already open (a tap action other than toggle, the key pressed again) or back from the file picker: it stays where it
        // is; placing it again could move an open chat to the monitor the mouse is on now.
        if (!wasShown) _geometry.Place();
        _shownAtMs = Environment.TickCount64;
        _reclaimed = 0;
        _shows++;
        Activate();
        if (!Native.ForceForeground(Hwnd)) _log.Error("could not take foreground; typing goes elsewhere until the panel is clicked");
        Shown?.Invoke();
        if (!wasShown) PlayOpenClose?.Invoke(true);
        CheckShown();
    }

    private int _showCheck;

    /// <summary>
    /// After a Modern Standby wake the panel can toggle "open" without appearing. Half a second after each open: if Windows
    /// says it's hidden or cloaked, or no frame was drawn, log what was seen and show it once more.
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
            if (check != _showCheck || !AppWindow.IsVisible || _geometry.Offscreen) return; // closed or reopened since
            var visible = Native.IsWindowVisible(Hwnd);
            var cloaked = Native.DwmGetWindowAttributeInt(Hwnd, Native.DWMWA_CLOAKED, out var c, sizeof(int)) == 0 && c != 0;
            var p = AppWindow.Position;
            var state = $"visible={visible} cloaked={cloaked} drawn={drawn} at {p.X},{p.Y} {AppWindow.Size.Width}x{AppWindow.Size.Height}";
            if (cloaked) _log.Info($"panel open but cloaked ({state})"); // e.g. another virtual desktop: leave it
            // The content must fit the client area (the window frame counts, or the bottom and the move grip are cut off).
            var needPx = _geometry.NeededClientPx;
            if (!_geometry.IsHandSized && !_geometry.IsAnimating && AppWindow.ClientSize.Height > 0 && AppWindow.ClientSize.Height + 1 < needPx)
                _log.Error($"panel content cut off: client {AppWindow.ClientSize.Height} px < content {needPx} px");
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

    private long _shownAtMs;
    private int _reclaimed;
    private int _shows;
    private long _consoleReclaimLaunch = long.MinValue;

    /// <summary>Self-test only: shows the panel far outside every monitor, without activation or focus, so the
    /// conversation is really drawn (composition) without the user seeing anything.</summary>
    public void ShowOffscreen()
    {
        _geometry.ParkOffscreen(); // no on-screen rect is current until the next placement
        // A realistic panel size for the primary monitor's scale (a fixed pixel size can be narrower than the button row).
        // Windows gives a window at (-32000, -32000) the scale of the monitor nearest it (not always the primary one).
        AppWindow.Move(new PointInt32(-32000, -32000));
        var size = (int)Math.Round(720 * OffscreenScale);
        AppWindow.MoveAndResize(new RectInt32(-32000, -32000, size, size));
        AppWindow.Show(activateWindow: false);
    }

    /// <summary>The scale of the monitor the off-screen window belongs to (its own DPI).</summary>
    private double OffscreenScale => Native.GetDpiForWindow(Hwnd) is var dpi and > 0 ? dpi / 96.0 : 1.0;

    /// <summary>Self-test only: the off-screen panel at another width (DIPs at the primary monitor's scale).</summary>
    public void ResizeOffscreen(int widthDip)
    {
        if (!_geometry.Offscreen) return;
        var scale = OffscreenScale;
        AppWindow.Resize(new SizeInt32((int)Math.Round(widthDip * scale), (int)Math.Round(720 * scale)));
    }

    public void EndOffscreen()
    {
        if (!_geometry.Offscreen) return; // a real open already ended it: never hide the panel the user is looking at
        AppWindow.Hide();
        _geometry.EndOffscreen();
    }

    /// <summary>Plays the open (true) or close (false) sound; only for a real open or close, never a re-show.</summary>
    public Action<bool>? PlayOpenClose { get; set; }

    /// <summary>The panel is about to close: a drag or keyboard move still running ends now, before the spot is decided.</summary>
    public event Action? Hiding;

    public void HidePopup()
    {
        if (!AppWindow.IsVisible) return;
        Hiding?.Invoke(); // a move ended by the close is part of this open: the pinned/forgotten decision below covers it
        PlayOpenClose?.Invoke(false);
        _geometry.Hidden(Pinned); // moved but not pinned (or forgotten): the next open uses the default
        // Closed while the panel (or the Copilot key's focus frame, 26H2) has focus: no window would have focus afterwards,
        // the rest of the key press would go nowhere (Windows' warning sound), and Windows would pick some window, maybe on
        // another monitor, which isn't the user moving. Hide without tracking that, then give focus back to where the panel
        // was opened from: the desktop or taskbar the user clicked, else the app it opened over. A close because the user
        // clicked elsewhere leaves focus there.
        var fg = Native.GetForegroundWindow();
        var fgClass = fg == 0 ? "" : Native.ClassNameOf(fg);
        var giveBack = ForegroundWindowPolicy.GivesFocusBackOnClose(fg == Hwnd, fgClass);
        using (_foreground.Suppress()) AppWindow.Hide();
        if (giveBack)
        {
            var prev = _foreground.RevalidateShowTarget(PreviousForeground);
            PreviousForeground = prev;
            var back = _foreground.UserLocation is var location and not 0 ? location : prev;
            if (back != 0)
            {
                if (Native.ForceForeground(back)) _log.Info($"focus back to {Native.ClassNameOf(back)}");
                else _log.Info("could not give focus back to the previous window");
            }
        }
        _guard.NoteHidden();
        Hidden?.Invoke();
    }

    /// <summary>Hides for a system dialog (file picker) without counting as a user dismissal.</summary>
    public void HideForDialog() { _hiddenForDialog = true; AppWindow.Hide(); }
    private bool _hiddenForDialog; // the next show is the panel coming back: no open sound

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
        else HidePopup(); // gives focus back (see HidePopup)
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
        _geometry.SetContentHeight(dip, conversation, barDip);
    }

    /// <summary>While held, focus loss (dialogs, captures) does not hide the popup.</summary>
    public IDisposable Modal()
    {
        _modal++;
        var suppress = _foreground.Suppress();
        return new Releaser(() => { suppress.Dispose(); _modal--; });
    }

    /// <summary>
    /// While held, the window keeps its size: resizing a window closes its open flyouts at once (the history list
    /// opened from the keyboard did exactly that). A resize or placement asked for meanwhile is applied when the last hold ends.
    /// Only flyouts take it; dialogs and captures still resize as usual.
    /// </summary>
    public IDisposable HoldResize()
    {
        _geometry.BeginHold();
        return new Releaser(() =>
        {
            if (DispatcherQueue.HasThreadAccess) _geometry.EndHold(); else DispatcherQueue.TryEnqueue(_geometry.EndHold);
        });
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
        if (e.WindowActivationState != WindowActivationState.Deactivated) _foreground.NotePanelFocused();
        if (e.WindowActivationState == WindowActivationState.Deactivated)
        {
            // Who took focus (the pinned-close sound and the forgotten spot were both about this moment).
            var taker = Native.GetForegroundWindow();
            _log.Info($"deactivated: focus went to {Describe(taker)} (pinned={Pinned})");
            // Windows 11 26H2: a Copilot key press first gives focus to Explorer's focus frame, then sends Hotline the
            // toggle ~0.1 s later. A panel still showing when the toggle hides it (pinned, or kept because Hotline's own
            // Settings window took focus) made Explorer play its warning sound. So any open panel closes now; the pinned
            // spot is kept, and the toggle that follows counts as this close. A long press reopens it through its action.
            if (AppWindow.IsVisible && _modal == 0 && Native.ClassNameOf(taker) == "CopilotKeyPressFocusFrame" && KeepOnKeyPress?.Invoke() != true)
            {
                _log.Info("Copilot key pressed while the panel is open: closing now");
                HidePopup();
                return;
            }
        }
        if (e.WindowActivationState == WindowActivationState.Deactivated && Pinned && _modal == 0 && _geometry.HasSessionSpot)
        {
            // Clicking into another app while pinned forgets the moved spot. Decided a moment later:
            // the Copilot key itself takes focus briefly (and then closes the panel), which is not clicking away.
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(600);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                if (!AppWindow.IsVisible || !Pinned) return; // closed by the key (or unpinned, which forgets anyway)
                var fg = Native.GetForegroundWindow();
                if (fg == 0 || fg == Hwnd || Hotline.Core.Windowing.ShellSurfaces.IsShell(Native.ClassNameOf(fg))) return;
                Native.GetWindowThreadProcessId(fg, out var pid);
                if (pid == Environment.ProcessId || IsOurProcess?.Invoke(pid) == true || IsHotline(pid)) return;
                _log.Info($"clicked into {Describe(fg)} while pinned");
                ForgetSessionSpot();
            };
            timer.Start();
        }
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

    /// <summary>
    /// True when a Copilot key press must not close the open panel early (set by the presenter): dictation is running
    /// (the key stops it), or the key's tap does something other than toggling the panel.
    /// </summary>
    public Func<bool>? KeepOnKeyPress { get; set; }

    /// <summary>Another Hotline process (the key's launcher instance) never counts as another app.</summary>
    private static bool IsHotline(uint pid) => ForegroundWindowTracker.IsHotlineExecutable(pid);

    /// <summary>A window for the log: class and process name.</summary>
    private static string Describe(nint hwnd)
    {
        if (hwnd == 0) return "none";
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        string name;
        try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); name = p.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { name = "?"; }
        return $"{Native.ClassNameOf(hwnd)} ({name})";
    }

    private Func<uint, bool>? _isOurProcess;
    /// <summary>Whether a process is one Hotline started (a CLI, or something it started); set by App.</summary>
    public Func<uint, bool>? IsOurProcess
    {
        get => _isOurProcess;
        set { _isOurProcess = value; _foreground.IsOurProcess = value; }
    }

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

    internal bool IsComposing { get; private set; }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsComposing || (uint)e.Key == 0xE5 || (uint)e.OriginalKey == 0xE5) return;
        if (e.Key == VirtualKey.Escape && !e.KeyStatus.WasKeyDown)
        {
            HidePopup();
            e.Handled = true;
        }
    }

    private void RecoverDisplay() => _geometry.RecoverDisplay();

    /// <summary>WM_DPICHANGED: answers with the planned rect when placement already used the new scale (see
    /// <see cref="PanelGeometry.AnswerDpiChange"/>); WinUI applies the rect and rescales its content.</summary>
    public bool OnDpiChanged(nint wParam, nint lParam)
    {
        var suggested = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.RECT>(lParam);
        var proposal = new RectI(suggested.Left, suggested.Top, suggested.Right - suggested.Left, suggested.Bottom - suggested.Top);
        var dpi = (uint)((long)wParam & 0xFFFF);
        if (_geometry.AnswerDpiChange(dpi, proposal) is { } answer)
            System.Runtime.InteropServices.Marshal.StructureToPtr(
                new Native.RECT { Left = answer.X, Top = answer.Y, Right = answer.X + answer.Width, Bottom = answer.Y + answer.Height }, lParam, false);
        return false; // WinUI applies the rect (and rescales its content)
    }

    // ---- user drags (Chat/PanelGrips) and the spot: see PanelGeometry ------------------------------

    /// <summary>The window's rectangle now (physical pixels).</summary>
    public RectI WindowRect => _geometry.WindowRect;
    public RectI WorkArea => _geometry.WorkArea;
    public bool IsResizeHeld => _geometry.IsResizeHeld;
    /// <summary>The scaled window.minWidth converted to real pixels on this monitor.</summary>
    public int MinDragWidthPx => _geometry.MinDragWidthPx;
    /// <summary>A resize may go down to the bar alone (the panel as it first opens).</summary>
    public int MinDragHeightPx => _geometry.MinDragHeightPx;
    public void BeginUserDrag() => _geometry.BeginUserDrag();
    public void DragMoveTo(int x, int y) => _geometry.DragMoveTo(x, y);
    public void DragResizeTo(RectI r) => _geometry.DragResizeTo(r);
    /// <summary>The drag ended: pulled inside its monitor, direction chosen again, spot kept for this session.</summary>
    public void EndUserDrag(bool resized, bool widthChanged = false) => _geometry.EndUserDrag(resized, widthChanged);
    /// <summary>Back to the saved default spot and size now (the grip's menu).</summary>
    public void BackToDefaultSpot() => _geometry.BackToDefaultSpot();
    /// <summary>What "Save as default" writes to state.json: anchor, width and chat height in scaled pixels, each over its settings.json value.</summary>
    public Hotline.Core.State.PanelSpot CurrentSpot() => _geometry.CurrentSpot();
    internal void RefreshSavedSpot() => _geometry.RefreshSavedSpot();
    /// <summary>The panel goes back to its saved default on the next open (unpinned, clicked into another app).</summary>
    public void ForgetSessionSpot() => _geometry.ForgetSessionSpot();

    /// <summary>The chat now grows up (true) or down: the presenter swaps the layout.</summary>
    public event Action<bool>? DirectionChanged
    {
        add => _geometry.DirectionChanged += value;
        remove => _geometry.DirectionChanged -= value;
    }
    public bool GrowsUp => _geometry.GrowsUp;

    /// <summary>The width (scaled pixels) the panel opens with on the monitor it would open on now: what Settings' "Move what
    /// doesn't fit into the menu" plans against, with the live width as the other bound.</summary>
    public double OpeningWidthDip() => _geometry.OpeningWidthDip();

    // ---- IPlacementSource ----------------------------------------------------------------------

    PlaceBy IPlacementSource.PlaceBy => _foreground.PlaceBy;
    nint IPlacementSource.RevalidatePrevious()
    {
        if (PreviousForeground != 0) PreviousForeground = _foreground.RevalidateShowTarget(PreviousForeground);
        return PreviousForeground;
    }
    nint IPlacementSource.UserMonitor => UserMonitor;
    string IPlacementSource.ClassName(nint window) => Native.ClassNameOf(window);

    /// <summary>The WinUI window as <see cref="PanelGeometry"/> sees it.</summary>
    private sealed class PanelWindowAdapter(PopupWindow w) : IPanelWindow
    {
        private long _lastFrame;
        public bool IsVisible => w.AppWindow.IsVisible;
        public RectI Rect => new(w.AppWindow.Position.X, w.AppWindow.Position.Y, w.AppWindow.Size.Width, w.AppWindow.Size.Height);
        public int FrameHeight => w.AppWindow.Size.Height - w.AppWindow.ClientSize.Height;
        public double PaddingDip => w.Root.Padding.Top + w.Root.Padding.Bottom;
        public nint Monitor => Native.MonitorFromWindow(w.Hwnd, Native.MONITOR_DEFAULTTONEAREST);
        public RectI? DisplayWork => DisplayArea.GetFromWindowId(w.AppWindow.Id, DisplayAreaFallback.Nearest) is { } area
            ? new RectI(area.WorkArea.X, area.WorkArea.Y, area.WorkArea.Width, area.WorkArea.Height) : null;
        public double? RasterizationScale => w.Root.XamlRoot?.RasterizationScale;
        public void MoveAndResize(RectI r) => w.AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
        public void Move(int x, int y) => w.AppWindow.Move(new PointInt32(x, y));

        public void StartFrames()
        {
            _lastFrame = System.Diagnostics.Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnRendering;
        }

        public void StopFrames() => CompositionTarget.Rendering -= OnRendering;

        private void OnRendering(object? sender, object e)
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var seconds = (now - _lastFrame) / (double)System.Diagnostics.Stopwatch.Frequency;
            _lastFrame = now;
            w._geometry.OnFrame(seconds);
        }
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
