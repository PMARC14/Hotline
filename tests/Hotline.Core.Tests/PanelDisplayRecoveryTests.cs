using Hotline.Core.Windowing;
using Xunit;

namespace Hotline.Core.Tests;

public class PanelDisplayRecoveryTests
{
    [Fact]
    public void Recovery_preserves_actual_size_and_scales_cached_bar_once()
    {
        var work = new RectI(0, 0, 1920, 1040);
        var actual = new RectI(100, 200, 900, 600);
        var r = PanelDisplayRecovery.Calculate(work, work, 1, 1.5, actual, new RectI(200, 700, 600, 60), 400, true);
        Assert.Equal(actual, r.Rect);
        Assert.Equal(600, r.FixedHeight);
        Assert.Equal(new RectI(100, 710, 900, 90), r.Bar); // 600 scaled pixels at 150%
        var again = PanelDisplayRecovery.Calculate(work, work, 1.5, 1.5, r.Rect, r.Bar, r.FixedHeight, true);
        Assert.Equal(r, again);
    }

    [Fact]
    public void Removed_monitor_clamps_actual_rect_into_surviving_work_area()
    {
        var r = PanelDisplayRecovery.Calculate(new RectI(1920, 0, 1920, 1040), new RectI(-1280, 40, 1280, 680),
            1.5, 1, new RectI(2200, 500, 1500, 900), new RectI(2300, 1300, 600, 90), 900, false);
        Assert.Equal(new RectI(-1280, 40, 1280, 680), r.Rect);
        Assert.Equal(new RectI(-840, 40, 400, 60), r.Bar);
        Assert.Equal(600, r.FixedHeight);
    }

    [Fact]
    public void Work_area_change_preserves_downward_baseline_and_unfixed_height()
    {
        var r = PanelDisplayRecovery.Calculate(new RectI(0, 0, 1000, 800), new RectI(40, 0, 960, 700),
            1, 1, new RectI(0, 500, 600, 300), new RectI(0, 500, 600, 60), null, false);
        Assert.Equal(new RectI(40, 400, 600, 300), r.Rect);
        Assert.Equal(new RectI(40, 400, 600, 60), r.Bar);
        Assert.Null(r.FixedHeight);
    }

    // WM_DPICHANGED: Windows proposes the window scaled by the DPI ratio. When Hotline placed the panel for that DPI already
    // (opening on a monitor of another scale), its own rect is the answer; otherwise (a drag across monitors, a scaling change
    // under a pinned panel) Windows' suggestion stands and RecoverDisplay rebuilds around it.
    [Fact]
    public void A_dpi_change_to_the_planned_scale_keeps_the_planned_rect()
    {
        var planned = new RectI(1320, 1529, 1200, 199);
        var suggested = new RectI(1320, 1529, 1800, 299);
        Assert.Equal(planned, PanelDisplayRecovery.RectForDpiChange(plannedScale: 1.5, newDpi: 144, planned, suggested, userMoving: false));
    }

    [Fact]
    public void A_dpi_change_to_another_scale_takes_windows_suggestion()
    {
        var planned = new RectI(1320, 1529, 1200, 199);
        var suggested = new RectI(4272, 1506, 800, 133);
        Assert.Equal(suggested, PanelDisplayRecovery.RectForDpiChange(1.5, 96, planned, suggested, false));
    }

    [Fact]
    public void A_drag_across_monitors_takes_windows_suggestion()
    {
        var planned = new RectI(1320, 1529, 1200, 199);
        var suggested = new RectI(1300, 1500, 1800, 299);
        Assert.Equal(suggested, PanelDisplayRecovery.RectForDpiChange(1.5, 144, planned, suggested, userMoving: true));
    }

    [Fact]
    public void Nothing_planned_takes_windows_suggestion()
    {
        var suggested = new RectI(0, 0, 900, 300);
        Assert.Equal(suggested, PanelDisplayRecovery.RectForDpiChange(1.5, 144, null, suggested, false));
    }
}
