using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class PanelPlacementTests
{
    private static readonly RectI Work = new(0, 0, 1920, 1040); // 1080p minus a bottom taskbar

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Default_anchor_reproduces_todays_place(double scale)
    {
        var bar = PanelPlacement.Home(Work, widthDip: 700, barHeightDip: 60, scale, new PanelAnchor());
        Assert.Equal(PopupGeometry.Place(Work, 700, 60, scale, 0.8), bar);
    }

    [Fact]
    public void Anchor_x_moves_the_centre_and_never_leaves_the_work_area()
    {
        var left = PanelPlacement.Home(Work, 700, 60, 1.0, new PanelAnchor { X = 0 });
        var right = PanelPlacement.Home(Work, 700, 60, 1.0, new PanelAnchor { X = 1 });
        Assert.Equal(0, left.X);
        Assert.Equal(Work.Width - 700, right.X);
        var top = PanelPlacement.Home(Work, 700, 60, 1.0, new PanelAnchor { Y = 0 });
        Assert.Equal(0, top.Y);
    }

    [Fact]
    public void Works_on_a_monitor_left_of_and_above_the_primary()
    {
        var work = new RectI(-2560, -400, 2560, 1400);
        var bar = PanelPlacement.Home(work, 800, 60, 1.25, new PanelAnchor { X = 0.5, Y = 0.5 });
        Assert.InRange(bar.X, work.X, work.X + work.Width - bar.Width);
        Assert.InRange(bar.Y, work.Y, work.Y + work.Height - bar.Height);
    }

    [Fact]
    public void Anchor_for_a_dropped_bar_round_trips()
    {
        var bar = new RectI(300, 200, 700, 60);
        var anchor = PanelPlacement.AnchorFor(bar, Work);
        Assert.Equal(bar, PanelPlacement.Home(Work, 700, 60, 1.0, anchor));
    }

    [Theory]
    [InlineData(800, 60, 500, true)]   // room above: up (the default)
    [InlineData(100, 60, 500, false)]  // no room above, room below: down
    [InlineData(500, 60, 500, true)]   // both fit: up
    [InlineData(300, 60, 900, false)]  // neither fits: the side with more room (below: 680 > 300)
    [InlineData(600, 60, 900, true)]   // neither fits: above has more (600 > 380)
    public void Auto_grows_up_unless_there_is_no_room_above(int barY, int barH, int chatPx, bool up)
        => Assert.Equal(up, PanelPlacement.GrowsUp(GrowDirection.Auto, new RectI(600, barY, 700, barH), Work, chatPx));

    [Fact]
    public void Locked_directions_never_swap()
    {
        Assert.True(PanelPlacement.GrowsUp(GrowDirection.Up, new RectI(0, 0, 700, 60), Work, 900));
        Assert.False(PanelPlacement.GrowsUp(GrowDirection.Down, new RectI(0, 980, 700, 60), Work, 900));
    }

    [Fact]
    public void Clamp_pulls_a_half_off_screen_rect_inside_and_shrinks_an_oversized_one()
    {
        Assert.Equal(new RectI(1220, 0, 700, 300), PanelPlacement.Clamp(new RectI(1500, -100, 700, 300), Work));
        Assert.Equal(new RectI(0, 0, 1920, 1040), PanelPlacement.Clamp(new RectI(-50, -50, 3000, 2000), Work));
    }

    [Fact]
    public void Resize_moves_only_the_dragged_edges_within_limits()
    {
        var start = new RectI(500, 300, 700, 400);
        Assert.Equal(new RectI(450, 300, 750, 400), PanelPlacement.Resize(start, ResizeEdge.Left, -50, 0, 600, 100, Work));
        Assert.Equal(new RectI(500, 300, 600, 400), PanelPlacement.Resize(start, ResizeEdge.Right, -500, 0, 600, 100, Work)); // min width
        Assert.Equal(new RectI(500, 250, 700, 450), PanelPlacement.Resize(start, ResizeEdge.Top, 0, -50, 600, 100, Work));
        Assert.Equal(new RectI(500, 300, 750, 450), PanelPlacement.Resize(start, ResizeEdge.Bottom | ResizeEdge.Right, 50, 50, 600, 100, Work));
        Assert.Equal(new RectI(500, 0, 700, 700), PanelPlacement.Resize(start, ResizeEdge.Top, 0, -900, 600, 100, Work)); // stops at the work area
    }

    [Fact]
    public void Rescale_keeps_the_dip_size_on_a_monitor_with_another_scale()
        => Assert.Equal(new RectI(100, 100, 1050, 150), PanelPlacement.Rescale(new RectI(100, 100, 700, 100), 1.0, 1.5));

    [Fact]
    public void Old_vertical_position_moves_into_the_anchor()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, SettingsStore.FileName), """{ "schemaVersion": 12, "window": { "verticalPosition": 0.3 } }""");
            var s = new SettingsStore(dir).Load();
            Assert.Equal(0.3, s.Window.Anchor.Y);
            Assert.DoesNotContain("verticalPosition", File.ReadAllText(Path.Combine(dir, SettingsStore.FileName)));
            Assert.Equal((GrowDirection.Auto, (int?)null, (int?)null, "active"), (s.Window.GrowDirection, s.Window.Width, s.Window.ChatHeight, s.Window.Anchor.Monitor));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Grow_down_keeps_the_bar_top_and_stays_on_screen()
    {
        var bar = new RectI(600, 100, 700, 60);
        Assert.Equal(new RectI(600, 100, 700, 400), PopupGeometry.GrowDown(bar, 400, 800, Work));
        Assert.Equal(new RectI(600, 100, 700, 60), PopupGeometry.GrowDown(bar, 10, 800, Work));
        Assert.Equal(new RectI(600, 100, 700, 940), PopupGeometry.GrowDown(bar, 2000, 2000, Work)); // the work area's bottom
    }
}
