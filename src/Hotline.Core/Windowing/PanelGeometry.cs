using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.State;

namespace Hotline.Core.Windowing;

/// <summary>
/// Where the panel is and how big: placement on open, the pinned session spot, height from the content (with the slide), the
/// chat's direction, user drags, display changes and WM_DPICHANGED. Moved out of PopupWindow unchanged; the window and the
/// monitors are reached only through <see cref="IPanelWindow"/> and <see cref="IMonitors"/>, so the order of these steps is
/// tested in Core. Not thread-safe: the App calls it on the UI thread.
/// </summary>
public sealed class PanelGeometry
{
    private readonly WindowSettings _settings;
    private readonly IPanelWindow _window;
    private readonly IMonitors _monitors;
    private readonly IPlacementSource _source;
    private readonly FileLog _log;
    private readonly StateStore? _state;
    private EffectivePanelSpot _resolvedSpot;
    private PanelLayoutSettings? _lastLayout;
    private bool _displayRecoveryHeld;
    private nint _displayMonitor;
    private RectI _bar;
    private RectI _work;
    private double _scale = 1;
    private double _contentDip;

    public PanelGeometry(WindowSettings settings, GrowMode growMode, IPanelWindow window, IMonitors monitors, IPlacementSource source,
        FileLog log, StateStore? state = null)
    {
        _settings = settings;
        GrowMode = growMode;
        _window = window;
        _monitors = monitors;
        _source = source;
        _log = log;
        _state = state;
        _resolvedSpot = PanelSpotPolicy.Resolve(_state, _settings);
    }

    public GrowMode GrowMode { get; set; }

    /// <summary>The self-test parks the window off-screen: no placement, height or display change touches it meanwhile.</summary>
    public bool Offscreen { get; private set; }

    /// <summary>The self-test parked the window off-screen: no on-screen rect is current until the next placement.</summary>
    public void ParkOffscreen()
    {
        Offscreen = true;
        _currentRect = null;
    }

    public void EndOffscreen() => Offscreen = false;

    /// <summary>The settings object changed (live): re-place when the layout settings changed, else re-size.</summary>
    public void SettingsChanged()
    {
        var next = PanelLayoutSettings.From(_settings);
        var changed = next != _lastLayout;
        _lastLayout = next;
        _resolvedSpot = PanelSpotPolicy.Resolve(_state, _settings);
        if (!_window.IsVisible) return;
        if (changed) RequestPlacement(); // deferred while HoldResize is held
        else ApplyHeight();
    }

    private void RequestPlacement()
    {
        if (_resizeHolds > 0) { _placementHeld = true; return; }
        PlaceOnActiveMonitor();
    }

    /// <summary>The panel opens (it wasn't showing): placed on the monitor the user is on, or its pinned spot.</summary>
    public void Place() => PlaceOnActiveMonitor();

    /// <summary>The panel closed: a moved spot is kept only while pinned (and not forgotten); held work is dropped.</summary>
    public void Hidden(bool pinned)
    {
        if (!pinned || _forgetOnHide) _session = null; // moved but not pinned (or forgotten): the next open uses the default
        _forgetOnHide = false;
        _displayRecoveryHeld = false;
        _placementHeld = false;
        _resizeHeld = false;
    }

    /// <summary>Content height (DIPs) of the conversation; the panel grows upward from its baseline.</summary>
    /// <param name="conversation">The chat has messages: the panel then expands to halfway at once, and past it with the text.</param>
    /// <param name="barDip">The bar alone (message box and button rows): kept in place when the chat grows down.</param>
    public void SetContentHeight(double dip, bool conversation, double barDip)
    {
        _contentDip = dip; // DIPs: converted with the current monitor scale in ApplyHeight
        _conversation = conversation;
        _barDip = barDip;
        if (_window.IsVisible) ApplyHeight();
    }

    // ---- resize holds (flyouts) ---------------------------------------------------------------

    /// <summary>A flyout opened: the window keeps its size until the last hold ends (see PopupWindow.HoldResize).</summary>
    public void BeginHold() => _resizeHolds++;

    /// <summary>A hold ended; the last one applies what was asked for meanwhile: display recovery, then placement, else height.</summary>
    public void EndHold()
    {
        _resizeHolds--;
        if (_resizeHolds != 0) return;
        if (!_window.IsVisible) { _placementHeld = false; _resizeHeld = false; return; }
        if (_displayRecoveryHeld) RecoverDisplay();
        if (_placementHeld) PlaceOnActiveMonitor();
        else if (_resizeHeld) ApplyHeight();
    }

    private int _resizeHolds;
    /// <summary>A resize was asked for during a HoldResize; any real resize settles it.</summary>
    private bool _resizeHeld;
    /// <summary>A placement was asked for during a HoldResize; consumed before height when the last hold ends.</summary>
    private bool _placementHeld;

    public bool IsResizeHeld => _resizeHolds > 0;

    // ---- display changes ----------------------------------------------------------------------

    /// <summary>The monitor, its work area or its scale changed under the panel: keep it inside, at the new scale.</summary>
    public void RecoverDisplay()
    {
        if (!_window.IsVisible || Offscreen || _bar.Width == 0) { _displayRecoveryHeld = false; return; }
        if (_resizeHolds > 0 || _dragging) { _displayRecoveryHeld = true; return; }
        _displayRecoveryHeld = false;
        var monitor = _window.Monitor;
        if (_window.DisplayWork is not { } work) return;
        var scale = _window.RasterizationScale ?? _scale;
        if (_displayMonitor == monitor && _work == work && Math.Abs(_scale - scale) < 0.001) return;
        _log.Info($"display change: {_scale} -> {scale}, window {WindowRect}");
        _displayMonitor = monitor;
        StopAnimation();
        var result = PanelDisplayRecovery.Calculate(_work, work, _scale, scale, WindowRect, _bar, _fixedPx, _growUp);
        (_work, _scale, _bar, _fixedPx) = (work, scale, result.Bar, result.FixedHeight);
        _downTop = _growUp ? null : result.Rect.Y;
        _currentRect = _targetRect = result.Rect;
        MoveTo(result.Rect);
        if (_session is not null) _session = new SessionSpot(_bar, _work, _scale, _fixedPx, _fixedWidth);
        ApplyHeight();
    }

    /// <summary>
    /// WM_DPICHANGED: Windows proposes the window scaled by the DPI ratio. When the panel was placed for the new DPI already
    /// (opening on a monitor of another scale), the answer is that rect instead: accepting the proposal scaled it twice
    /// (1.5x too big from 100 % to 150 %). A drag across monitors or a scaling change keeps Windows' proposal. Null: keep it.
    /// </summary>
    public RectI? AnswerDpiChange(uint dpi, RectI proposal)
    {
        if (Offscreen) return null; // the self-test window parked at -32000: Windows' rect stands
        var answer = PanelDisplayRecovery.RectForDpiChange(_scale, dpi, _currentRect, proposal, _dragging);
        if (answer == proposal) return null;
        _log.Info($"place: DPI change to {dpi}: keeping {answer} (Windows proposed {proposal})");
        return answer;
    }

    // ---- height -------------------------------------------------------------------------------

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
        if (Offscreen) return; // never move the off-screen self-test window onto a monitor
        if (_resizeHolds > 0 && !force) { _resizeHeld = true; return; } // a flyout is open: see HoldResize
        if (_bar.Width == 0 || _dragging) return;
        _resizeHeld = false; // this resize settles any held one
        var maxPx = MaxPx;
        var bar = _growUp ? _bar : DownBar;
        // Before the first layout pass there is no measured content yet: keep a sane minimum instead of 0 px.
        var contentPx = (int)Math.Ceiling(Math.Max(_contentDip, MinContentDip) * _scale) + FramePx;
        if (_fixedPx is { } fixedPx) contentPx = fixedPx; // resized by hand: that height until the next open
        else if (GrowMode == GrowMode.Grow)
        {
            // The floor is the bar's real height in both directions (growing up, _bar.Height is window.minHeight, 0 by
            // default, which made the first "halfway" step max/2 up but bar + (max - bar)/2 down).
            var floorPx = BarPx;
            var previous = _targetRect.Height > 0 ? _targetRect.Height : floorPx;
            contentPx = PopupGeometry.TwoStep(previous, contentPx, floorPx, Math.Max(floorPx, maxPx), _conversation);
        }
        // Full height only during a conversation and not over a size set by hand (see PopupGeometry.FullHeight).
        var manual = _fixedPx is not null;
        _targetRect = _growUp ? PopupGeometry.GrowUp(bar, contentPx, maxPx, _work, GrowMode, _conversation, manual)
            : PopupGeometry.GrowDown(bar, contentPx, maxPx, _work, GrowMode, _conversation, manual);
        // Once a chat has started the panel may also widen (window.chatWidth*), around its centre: the same resize as the height's,
        // never a separate one. Shrinking back is the same call without a conversation.
        if (_conversation && ChatWidthPx is { } widePx) _targetRect = PopupGeometry.GrowWidth(_targetRect, widePx, _work);
        _widthGrown = _targetRect.Width > bar.Width;
        // During a chat the panel slides to its new height; anything else (no message yet, a new chat, placing) moves at once.
        // The bar's edge stays put: the bottom when growing up, the top when growing down.
        if (!force && _conversation && GrowMode == GrowMode.Grow && _window.IsVisible && _currentRect is { } current
            && current.Width == _targetRect.Width
            && (_growUp ? current.Y + current.Height == _targetRect.Y + _targetRect.Height : current.Y == _targetRect.Y))
        {
            if (current == _targetRect || _animating) return; // already there, or the running slide retargets next frame
            _animating = true;
            _animatedHeight = current.Height;
            _window.StartFrames();
            return;
        }
        StopAnimation();
        if (force || _currentRect != _targetRect) MoveTo(_targetRect); // force: placing it anew (the window may have been moved)
    }

    /// <summary>The panel is wider than the bar it opened at (the chat width): a move then keeps the start width as the bar's.</summary>
    private bool _widthGrown;

    /// <summary>The chat width in real pixels, or null when disabled, the width was resized by hand, or no wider than the opening width.</summary>
    private int? ChatWidthPx => _fixedWidth || _settings.ChatWidth is null ? null
        : PanelWidth.ResolvePixels(_settings, _work.Width, _scale, chat: true) is var width && width > _bar.Width ? width : null;

    private bool _animating;
    private double _animatedHeight;

    /// <summary>A height slide is running (the content may be taller than the window for a moment).</summary>
    public bool IsAnimating => _animating;

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        _window.StopFrames();
    }

    /// <summary>
    /// One display frame of a slide: exponential ease toward the target height (time constant ~70 ms, frame-rate
    /// independent), bottom edge fixed. Runs on the compositor's frame clock so steps line up with screen refreshes.
    /// </summary>
    /// <param name="seconds">Since the slide started or the last frame.</param>
    public void OnFrame(double seconds)
    {
        var dt = Math.Clamp(seconds, 0.001, 0.1);
        if (!_window.IsVisible) { StopAnimation(); return; }
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
        _window.MoveAndResize(r);
    }

    /// <summary>The least content height (DIPs) assumed before the first layout pass: the panel's own top and bottom padding plus one control row.</summary>
    private double MinContentDip => _window.PaddingDip + 48;

    private double _barDip;
    private bool _growUp = true;

    /// <summary>
    /// The window's frame height (window minus client area): the window rectangle must be this much taller than the
    /// content, or the bottom of the panel (its padding, the move grip) is cut off. 13 px at 200 % on Windows 11.
    /// </summary>
    private int FramePx => Math.Max(0, _window.FrameHeight);

    /// <summary>The bar alone as a window height, in pixels (measured; a sane minimum before the first layout).</summary>
    private int BarPx => Math.Max(_bar.Height, (int)Math.Ceiling(Math.Max(_barDip, MinContentDip) * _scale) + FramePx);

    /// <summary>The client height the content needs now (capped at the tallest the panel may be): the open check compares it.</summary>
    public int NeededClientPx => Math.Min((int)Math.Ceiling(Math.Max(_contentDip, MinContentDip) * _scale), MaxPx - FramePx);

    /// <summary>The height was set by hand this opening (content no longer sizes the panel).</summary>
    public bool IsHandSized => _fixedPx is not null;

    /// <summary>
    /// The bar's rectangle: its bottom edge is the placement line in both directions, kept inside the work area (a line
    /// near the top has no room for the bar above it).
    /// </summary>
    private RectI DownBar => PanelPlacement.Clamp(new RectI(_bar.X, _downTop ?? _bar.Y + _bar.Height - BarPx, _bar.Width, BarPx), _work);

    /// <summary>Growing down: the bar's top edge, fixed when the direction is chosen (so typing more lines grows it downward).</summary>
    private int? _downTop;

    /// <summary>The tallest the panel may be: the bar plus effective chatHeight, else maxHeightPercent of the work area.</summary>
    private int MaxPx => Math.Min(_work.Height, _resolvedSpot.ChatHeight is { } chat
        ? BarPx + (int)Math.Round(chat * _scale)
        : (int)Math.Round(_work.Height * _settings.MaxHeightPercent / 100.0));

    /// <summary>The spot the panel was moved/resized to this session; reused only while pinned (never saved).</summary>
    private sealed record SessionSpot(RectI Bar, RectI Work, double Scale, int? FixedPx, bool FixedWidth);
    private SessionSpot? _session;
    /// <summary>A resize fixes the panel's height for this opening (content no longer resizes it).</summary>
    private int? _fixedPx;
    /// <summary>A resize by hand changed the panel's width this opening: it then keeps that width (no chat width over it), like a hand height.</summary>
    private bool _fixedWidth;

    /// <summary>A moved spot is kept for this session (used while pinned).</summary>
    public bool HasSessionSpot => _session is not null;

    // ---- user drags (Chat/PanelGrips) ---------------------------------------------------------

    /// <summary>The window's rectangle now (physical pixels).</summary>
    public RectI WindowRect => _window.Rect;
    public RectI WorkArea => _work;
    /// <summary>The scaled window.minWidth converted to real pixels on this monitor.</summary>
    public int MinDragWidthPx => PanelWidth.MinimumPixels(_settings, _scale);
    /// <summary>A resize may go down to the bar alone (the panel as it first opens).</summary>
    public int MinDragHeightPx => PanelPlacement.MinResizeHeight(BarPx);
    private bool _dragging;

    public void BeginUserDrag()
    {
        _dragging = true;
        StopAnimation();
    }

    public void DragMoveTo(int x, int y)
    {
        _window.Move(x, y);
        _currentRect = WindowRect;
    }

    public void DragResizeTo(RectI r) => MoveTo(r);

    /// <summary>
    /// The drag ended: the window is pulled inside the monitor it's on, the bar line follows it (the bar's bottom edge),
    /// the direction is chosen again, and the spot is kept for this session (used while pinned).
    /// </summary>
    public void EndUserDrag(bool resized, bool widthChanged = false)
    {
        _dragging = false;
        if (Offscreen) return;
        var rect = WindowRect;
        if (_monitors.AtRect(rect, nearest: true) is not { } m) return;
        var oldScale = _scale;
        (_work, _scale) = (m.Work, m.Scale); // the monitor it's pulled onto decides the scale
        if (_fixedPx is { } kept && !resized && Math.Abs(m.Scale - oldScale) > 0.001) _fixedPx = (int)Math.Round(kept * m.Scale / oldScale);
        rect = PanelPlacement.Clamp(rect, _work);
        if (resized) _fixedPx = rect.Height;
        if (widthChanged) _fixedWidth = true;
        var h = (int)Math.Round(_settings.MinHeight * _scale);
        // A move or height drag of a panel widened for a chat keeps the bar's own width (centred), so it shrinks back to it later.
        var (barX, barW) = !widthChanged && _widthGrown && _bar.Width < rect.Width ? (rect.X + (rect.Width - _bar.Width) / 2, _bar.Width) : (rect.X, rect.Width);
        _bar = new RectI(barX, rect.Y, barW, h); // BarPx below reads this monitor's bar height
        // The bar line where the window now shows its bar: at the bottom when growing up, at the top when growing down.
        void BarAt(bool up)
        {
            var line = up ? rect.Y + rect.Height : rect.Y + Math.Min(BarPx, rect.Height);
            _bar = new RectI(barX, line - h, barW, h);
        }
        BarAt(_growUp);
        var wasUp = _growUp;
        ChooseDirection();
        if (_growUp != wasUp)
        {
            // Flipped: keep the window where it was dropped, the bar now on its other edge.
            BarAt(_growUp);
            _downTop = _growUp ? null : rect.Y;
        }
        _session = new SessionSpot(_bar, _work, _scale, _fixedPx, _fixedWidth);
        _forgetOnHide = false; // moved again: this is the spot now
        _displayMonitor = m.Handle;
        _displayRecoveryHeld = false; // drag completion rebuilt geometry using the surviving display
        ApplyHeight(force: true);
    }

    /// <summary>Back to the saved default spot and size now (the grip's menu).</summary>
    public void BackToDefaultSpot()
    {
        _session = null;
        _fixedPx = null;
        _fixedWidth = false;
        RequestPlacement();
    }

    /// <summary>What "Save as default" writes to state.json: anchor, width and chat height in scaled pixels, each over its settings.json value.</summary>
    public PanelSpot CurrentSpot() => PanelSpotPolicy.BuildSaveSpot(_bar, _work, _scale, _fixedPx, BarPx, _settings, _state);

    public void RefreshSavedSpot() => _resolvedSpot = PanelSpotPolicy.Resolve(_state, _settings);

    /// <summary>The panel goes back to its saved default on the next open (unpinned, clicked into another app).</summary>
    public void ForgetSessionSpot()
    {
        if (_session is null) return;
        // Open: it stays where it is until it closes (a settings change while open must not move it); then forgotten.
        if (_window.IsVisible) _forgetOnHide = true; else _session = null;
        _log.Info("pinned spot forgotten: the next open uses the default");
    }

    private bool _forgetOnHide;

    /// <summary>Picks the chat's direction for the current bar and lays the panel out for it.</summary>
    private void ChooseDirection()
    {
        var chatPx = Math.Max(0, MaxPx - BarPx);
        _downTop = null; // from this bar line, not the last placement's (a stale top once picked "down" at the default spot)
        var up = PanelPlacement.GrowsUp(_settings.GrowDirection, DownBar, _work, chatPx);
        if (!up) _downTop = DownBar.Y; // now, from the bar line
        if (up == _growUp) return;
        _growUp = up;
        DirectionChanged?.Invoke(up);
    }

    /// <summary>The chat now grows up (true) or down: the presenter swaps the layout.</summary>
    public event Action<bool>? DirectionChanged;
    public bool GrowsUp => _growUp;

    // ---- placement ----------------------------------------------------------------------------

    private void PlaceOnActiveMonitor()
    {
        _placementHeld = false;
        // A moved spot: kept across closes only while pinned (Hidden drops it otherwise), and kept while open, so a
        // settings change (a model picked in the bar) doesn't snap a moved panel back to its default spot.
        if (_session is { } spot && _monitors.AtRect(spot.Work, nearest: false) is { } still)
        {
            // The same spot (pulled inside its monitor again, in case its work area changed).
            (_work, _scale) = (still.Work, still.Scale);
            _fixedPx = spot.FixedPx is { } f ? (int)Math.Round(f * still.Scale / spot.Scale) : null;
            _fixedWidth = spot.FixedWidth;
            _bar = PanelPlacement.Clamp(PanelPlacement.Rescale(spot.Bar, spot.Scale, still.Scale), still.Work);
            ChooseDirection();
            _log.Info($"place: pinned spot {_bar} on {still.Device} scale={still.Scale} up={_growUp}");
            ApplyHeight(force: true);
            _displayMonitor = _window.Monitor;
            return;
        }
        _fixedPx = null;
        _fixedWidth = false;
        _resolvedSpot = PanelSpotPolicy.Resolve(_state, _settings);
        var (home, how) = HomeMonitor();
        var (work, scale) = (home.Work, home.Scale);
        var widthPx = _resolvedSpot.OpeningWidthPixels(_settings, work.Width, scale);
        var r = PanelPlacement.Home(work, widthPx, _settings.MinHeight, scale, _resolvedSpot.Anchor);
        (_bar, _work, _scale) = (r, work, scale);
        _displayMonitor = home.Handle;
        ChooseDirection();
        _log.Info($"place: {how} {home.Device} work={work.X},{work.Y} {work.Width}x{work.Height} scale={scale} anchor={_resolvedSpot.Anchor.X:0.###},{_resolvedSpot.Anchor.Y:0.###} -> {r} up={_growUp}");
        ApplyHeight(force: true);
    }

    /// <summary>The width (scaled pixels) the panel opens with on the monitor it would open on now (the resolved width setting or saved spot,
    /// or its percentage of that monitor's work area): what Settings' "Move what doesn't fit into the menu" plans against, with the live width as the other bound.</summary>
    public double OpeningWidthDip()
    {
        _resolvedSpot = PanelSpotPolicy.Resolve(_state, _settings);
        var (home, _) = HomeMonitor();
        var px = _resolvedSpot.OpeningWidthPixels(_settings, home.Work.Width, home.Scale);
        return px / home.Scale;
    }

    /// <summary>The monitor for the saved default: the one you're working on, the primary one, or a named one.</summary>
    private (MonitorInfo Monitor, string How) HomeMonitor()
    {
        var wanted = _resolvedSpot.Anchor.Monitor;
        if (string.Equals(wanted, PanelAnchor.Primary, StringComparison.OrdinalIgnoreCase)) return (_monitors.Primary, "primary");
        if (!string.Equals(wanted, PanelAnchor.Active, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var m in _monitors.All)
                if (string.Equals(m.Device, wanted, StringComparison.OrdinalIgnoreCase)) return (m, "saved monitor");
            _log.Info($"saved monitor {wanted} isn't connected; using the active one");
        }
        // A remembered window may have closed since (stale handle): its monitor/DPI lookups then fail and the
        // popup came out at 100% scale ("occasionally small"). Only trust live windows; otherwise use the cursor.
        var previous = _source.RevalidatePrevious();
        // After a click on another monitor's empty desktop or taskbar, the remembered app is on the wrong monitor: use the cursor.
        var anchor = _source.PlaceBy == PlaceBy.Cursor ? 0 : previous;
        var home = anchor != 0 ? _monitors.OfWindow(anchor) : null;
        // Already open after a desktop/taskbar click: placed again (a settings change, Back to the default spot), it stays on
        // the monitor it opened on rather than following the mouse.
        if (home is null && _window.IsVisible && !Offscreen && _monitors.Valid(_source.UserMonitor) is { } opened) home = opened;
        if (home is null)
        {
            if (anchor != 0) _log.Error($"no display area for previous foreground window 0x{anchor:X} ({_source.ClassName(anchor)}); using cursor monitor");
            home = _monitors.AtCursor();
        }
        return (home.Value, anchor == 0 && previous != 0 ? "active (cursor)" : "active");
    }
}
