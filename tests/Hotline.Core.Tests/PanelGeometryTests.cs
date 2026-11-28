using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>
/// The panel's placement and sizing sequence (moved out of PopupWindow unchanged), on two monitors like the owner's: a
/// landscape 4K at 150 % and a portrait one at 100 % to its right. Pins today's behaviour before the split goes further.
/// </summary>
public sealed class PanelGeometryTests : TempDirTest
{
    private static readonly MonitorInfo Big = new(1, new RectI(0, 0, 3840, 2100), 1.5, @"\\.\DISPLAY1");
    private static readonly MonitorInfo Tall = new(2, new RectI(3840, 0, 1440, 2520), 1.0, @"\\.\DISPLAY2");
    private const nint AppOnBig = 0x100, AppOnTall = 0x200;

    private sealed class Monitors : IMonitors
    {
        public List<MonitorInfo> List { get; } = [Big, Tall];
        public (int X, int Y) Cursor { get; set; } = (100, 100);
        public Dictionary<nint, nint> Windows { get; } = new() { [AppOnBig] = 1, [AppOnTall] = 2 };
        public MonitorInfo Primary => List[0];
        public IEnumerable<MonitorInfo> All => List;
        public MonitorInfo? OfWindow(nint window) => Windows.TryGetValue(window, out var m) ? Valid(m) : null;
        public MonitorInfo AtCursor() => AtRect(new RectI(Cursor.X, Cursor.Y, 1, 1), nearest: true)!.Value;
        public MonitorInfo? Valid(nint monitor) => List.Where(m => m.Handle == monitor).Select(m => (MonitorInfo?)m).FirstOrDefault();

        public MonitorInfo? AtRect(RectI r, bool nearest)
        {
            static long Overlap(RectI a, RectI b) =>
                (long)Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X))
                * Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
            var best = List.OrderByDescending(m => Overlap(m.Work, r)).First();
            if (Overlap(best.Work, r) > 0) return best;
            if (!nearest) return null;
            double Distance(MonitorInfo m) => Math.Abs(m.Work.X + m.Work.Width / 2.0 - (r.X + r.Width / 2.0));
            return List.OrderBy(Distance).First();
        }
    }

    private sealed class Window(Monitors monitors) : IPanelWindow
    {
        public bool IsVisible { get; set; }
        public RectI Rect { get; set; }
        public int FrameHeight => 13;
        public double PaddingDip => 20;
        public nint Monitor => monitors.AtRect(Rect, nearest: true)!.Value.Handle;
        public RectI? DisplayWork => monitors.AtRect(Rect, nearest: true)?.Work;
        public double? RasterizationScale { get; set; }
        public List<RectI> Moves { get; } = [];
        public int FramesStarted { get; private set; }
        public bool Framing { get; private set; }
        public void MoveAndResize(RectI rect) { Rect = rect; Moves.Add(rect); }
        public void Move(int x, int y) => Rect = Rect with { X = x, Y = y };
        public void StartFrames() { FramesStarted++; Framing = true; }
        public void StopFrames() => Framing = false;
    }

    private sealed class Source : IPlacementSource
    {
        public PlaceBy PlaceBy { get; set; }
        public nint Previous { get; set; }
        public nint RevalidatePrevious() => Previous;
        public nint UserMonitor { get; set; }
        public string ClassName(nint window) => "App";
    }

    private readonly Monitors _monitors = new();
    private readonly Source _source = new();
    private readonly WindowSettings _settings = new();
    private Window _window = null!;

    private PanelGeometry Geometry(GrowMode mode = GrowMode.Grow)
    {
        Directory.CreateDirectory(_dir);
        _window = new Window(_monitors);
        var g = new PanelGeometry(_settings, mode, _window, _monitors, _source, new FileLog(Path.Combine(_dir, "h.log")));
        g.SettingsChanged(); // as PopupWindow's ApplyAppearance does at startup (hidden: nothing moves)
        return g;
    }

    /// <summary>Opens the panel the way ShowPopup does: placed while hidden, then shown.</summary>
    private void Open(PanelGeometry g)
    {
        g.Place();
        _window.IsVisible = true;
    }

    private bool On(MonitorInfo m, RectI r) => _monitors.AtRect(r, nearest: false)?.Handle == m.Handle
        && r.X >= m.Work.X && r.Y >= m.Work.Y && r.X + r.Width <= m.Work.X + m.Work.Width && r.Y + r.Height <= m.Work.Y + m.Work.Height;

    [Fact]
    public void Nothing_moves_while_hidden_or_before_the_first_placement()
    {
        var g = Geometry();
        g.SetContentHeight(300, conversation: false, barDip: 120);
        g.RecoverDisplay();
        Assert.Empty(_window.Moves);
    }

    [Fact]
    public void An_open_goes_to_the_remembered_apps_monitor_at_its_scale()
    {
        _source.Previous = AppOnTall;
        var g = Geometry();
        Open(g);
        Assert.True(On(Tall, _window.Rect));
        Assert.Equal(Tall.Work, g.WorkArea);
        Assert.Equal((int)Math.Round(Tall.Work.Width * 0.4), _window.Rect.Width); // window.width 40 % at 100 %
    }

    [Fact]
    public void After_a_desktop_click_an_open_follows_the_cursor()
    {
        _source.Previous = AppOnTall;
        _source.PlaceBy = PlaceBy.Cursor;
        _monitors.Cursor = (200, 300); // on the big monitor
        var g = Geometry();
        Open(g);
        Assert.True(On(Big, _window.Rect));
    }

    [Fact]
    public void A_closed_remembered_window_falls_back_to_the_cursor()
    {
        _source.Previous = 0x999; // not a window any more
        _monitors.Cursor = (4000, 100);
        var g = Geometry();
        Open(g);
        Assert.True(On(Tall, _window.Rect));
    }

    [Fact]
    public void Placed_again_while_open_after_a_desktop_click_it_stays_on_the_monitor_it_opened_on()
    {
        _source.PlaceBy = PlaceBy.Cursor;
        _monitors.Cursor = (4000, 100);
        _source.UserMonitor = Tall.Handle;
        var g = Geometry();
        Open(g);
        _monitors.Cursor = (100, 100); // the mouse went to the other monitor; the cursor no longer decides
        _source.PlaceBy = PlaceBy.RememberedWindow;
        _source.Previous = 0;
        g.BackToDefaultSpot();
        Assert.True(On(Tall, _window.Rect));
    }

    [Fact]
    public void A_primary_or_named_anchor_monitor_wins_over_where_the_user_is()
    {
        _source.Previous = AppOnTall;
        _settings.Anchor = new PanelAnchor { Monitor = PanelAnchor.Primary };
        var g = Geometry();
        Open(g);
        Assert.True(On(Big, _window.Rect));

        _settings.Anchor = new PanelAnchor { Monitor = Tall.Device! };
        _source.Previous = AppOnBig;
        var h = Geometry();
        Open(h);
        Assert.True(On(Tall, _window.Rect));
    }

    [Fact]
    public void Placement_on_a_monitor_of_another_scale_answers_WM_DPICHANGED_with_the_planned_rect()
    {
        _source.Previous = AppOnBig;
        var g = Geometry();
        Open(g);
        var planned = _window.Rect;
        var proposal = planned with { Width = planned.Width * 2 / 3, Height = planned.Height * 2 / 3 };
        Assert.Equal(planned, g.AnswerDpiChange(144, proposal)); // placed for 150 % already
        Assert.Null(g.AnswerDpiChange(96, proposal)); // another scale: Windows' proposal stands
        g.BeginUserDrag();
        Assert.Null(g.AnswerDpiChange(144, proposal)); // a drag across monitors keeps the proposal
    }

    [Fact]
    public void The_offscreen_self_test_window_is_never_placed_sized_or_answered()
    {
        var g = Geometry();
        Open(g);
        g.ParkOffscreen();
        var moves = _window.Moves.Count;
        g.SetContentHeight(800, conversation: true, barDip: 120);
        g.SettingsChanged();
        g.RecoverDisplay();
        Assert.Null(g.AnswerDpiChange(144, new RectI(0, 0, 10, 10)));
        g.EndUserDrag(resized: true);
        Assert.Equal(moves, _window.Moves.Count);
        g.EndOffscreen();
        Assert.False(g.Offscreen);
    }

    [Fact]
    public void Growing_up_and_down_take_the_same_first_step_from_the_bars_real_height()
    {
        RectI FirstStep(GrowDirection direction)
        {
            _settings.GrowDirection = direction;
            _source.Previous = AppOnTall;
            var g = Geometry();
            g.SetContentHeight(120, conversation: false, barDip: 120);
            Open(g);
            g.SetContentHeight(2000, conversation: true, barDip: 120);
            return _window.Rect;
        }
        var up = FirstStep(GrowDirection.Up);
        var down = FirstStep(GrowDirection.Down);
        Assert.Equal(up.Height, down.Height);
    }

    [Fact]
    public void Content_growing_during_a_chat_slides_and_settles_on_the_target()
    {
        _source.Previous = AppOnTall;
        var g = Geometry();
        g.SetContentHeight(120, conversation: false, barDip: 120);
        Open(g);
        g.SetContentHeight(200, conversation: true, barDip: 120); // the first message: halfway at once
        var start = _window.Rect;
        g.SetContentHeight(2000, conversation: true, barDip: 120);
        Assert.True(g.IsAnimating);
        Assert.Equal(1, _window.FramesStarted);
        for (var i = 0; i < 200 && g.IsAnimating; i++) g.OnFrame(1 / 60.0);
        Assert.False(g.IsAnimating);
        Assert.False(_window.Framing);
        Assert.True(_window.Rect.Height >= start.Height);
        Assert.Equal(start.Y + start.Height, _window.Rect.Y + _window.Rect.Height); // grew up: the bottom stayed
    }

    [Fact]
    public void A_flyout_hold_defers_placement_then_height_until_the_last_hold_ends()
    {
        _source.Previous = AppOnTall;
        var g = Geometry();
        Open(g);
        var moves = _window.Moves.Count;
        g.BeginHold();
        g.BeginHold();
        _settings.Width = new PanelWidth(50, true); // a layout change: placed again
        g.SettingsChanged();
        g.SetContentHeight(400, conversation: false, barDip: 120);
        Assert.Equal(moves, _window.Moves.Count);
        g.EndHold();
        Assert.Equal(moves, _window.Moves.Count); // one hold still open
        g.EndHold();
        Assert.Equal((int)Math.Round(Tall.Work.Width * 0.5), _window.Rect.Width);
        Assert.False(g.IsResizeHeld);
    }

    [Fact]
    public void A_hold_that_ends_after_the_panel_closed_drops_what_it_held()
    {
        _source.Previous = AppOnTall;
        var g = Geometry();
        Open(g);
        g.BeginHold();
        _settings.Width = new PanelWidth(60, true);
        g.SettingsChanged();
        _window.IsVisible = false;
        var moves = _window.Moves.Count;
        g.EndHold();
        Assert.Equal(moves, _window.Moves.Count);
    }

    [Fact]
    public void A_pinned_moved_spot_is_reused_and_an_unpinned_one_is_dropped_on_close()
    {
        _source.Previous = AppOnTall;
        var g = Geometry();
        Open(g);
        var home = _window.Rect;
        g.BeginUserDrag();
        g.DragMoveTo(home.X, home.Y - 400);
        g.EndUserDrag(resized: false); // a drag still running when the panel closes ends first (PopupWindow.Hiding)
        var moved = _window.Rect;
        Assert.NotEqual(home, moved);

        _window.IsVisible = false;
        g.Hidden(pinned: true);
        Open(g);
        Assert.Equal(moved, _window.Rect);

        _window.IsVisible = false;
        g.Hidden(pinned: false);
        Assert.False(g.HasSessionSpot);
        Open(g);
        Assert.Equal(home, _window.Rect);
    }

    [Fact]
    public void Forgetting_the_spot_while_open_keeps_the_panel_where_it_is_until_it_closes()
    {
        _source.Previous = AppOnTall;
        var g = Geometry();
        Open(g);
        var home = _window.Rect;
        g.BeginUserDrag();
        g.DragMoveTo(home.X, home.Y - 400);
        g.EndUserDrag(resized: false);
        var moved = _window.Rect;
        g.ForgetSessionSpot();
        Assert.True(g.HasSessionSpot);
        g.SetContentHeight(130, conversation: false, barDip: 120);
        Assert.Equal(moved.Y + moved.Height, _window.Rect.Y + _window.Rect.Height); // still at the moved spot
        _window.IsVisible = false;
        g.Hidden(pinned: true); // pinned, but forgotten
        Open(g);
        Assert.Equal((home.X, home.Width, home.Y + home.Height), (_window.Rect.X, _window.Rect.Width, _window.Rect.Y + _window.Rect.Height));
    }

    [Fact]
    public void A_hand_height_is_rescaled_when_a_move_takes_the_panel_to_a_monitor_of_another_scale()
    {
        _source.Previous = AppOnBig;
        var g = Geometry();
        Open(g);
        var r = _window.Rect;
        g.BeginUserDrag();
        g.DragResizeTo(r with { Y = r.Y - 600, Height = r.Height + 600 });
        g.EndUserDrag(resized: true);
        var handPx = _window.Rect.Height;
        Assert.True(g.IsHandSized);

        g.BeginUserDrag();
        g.DragMoveTo(Tall.Work.X + 100, 400); // onto the 100 % monitor
        g.EndUserDrag(resized: false);
        Assert.Equal(Tall.Work, g.WorkArea);
        Assert.Equal((int)Math.Round(handPx * 1.0 / 1.5), _window.Rect.Height);
    }

    [Fact]
    public void A_display_change_keeps_the_panel_inside_the_new_work_area_and_waits_for_a_hold()
    {
        _source.Previous = AppOnTall;
        var g = Geometry();
        Open(g);
        var before = _window.Moves.Count;
        g.RecoverDisplay(); // nothing changed
        Assert.Equal(before, _window.Moves.Count);

        _monitors.List[1] = Tall with { Work = Tall.Work with { Height = 2000 } }; // the taskbar grew
        g.BeginHold();
        g.RecoverDisplay();
        Assert.Equal(before, _window.Moves.Count); // held while a flyout is open
        g.EndHold();
        Assert.True(_window.Moves.Count > before);
        Assert.True(_window.Rect.Y + _window.Rect.Height <= 2000);
        Assert.Equal(2000, g.WorkArea.Height);
    }

    [Fact]
    public void A_drag_end_flips_the_direction_near_the_top_of_the_monitor()
    {
        _source.Previous = AppOnTall;
        var g = Geometry();
        var flips = new List<bool>();
        g.DirectionChanged += flips.Add;
        Open(g);
        Assert.True(g.GrowsUp);
        g.BeginUserDrag();
        g.DragMoveTo(_window.Rect.X, 0);
        g.EndUserDrag(resized: false);
        Assert.False(g.GrowsUp);
        Assert.Equal([false], flips);
        Assert.Equal(0, _window.Rect.Y); // kept where it was dropped, the bar now on its top edge
    }

    [Fact]
    public void The_needed_client_height_follows_content_and_caps_at_the_maximum()
    {
        _source.Previous = AppOnTall;
        var g = Geometry();
        Open(g);
        g.SetContentHeight(300, conversation: false, barDip: 120);
        Assert.Equal(300, g.NeededClientPx);
        g.SetContentHeight(100_000, conversation: true, barDip: 120);
        Assert.Equal((int)Math.Round(Tall.Work.Height * _settings.MaxHeightPercent / 100.0) - 13, g.NeededClientPx);
    }
}
