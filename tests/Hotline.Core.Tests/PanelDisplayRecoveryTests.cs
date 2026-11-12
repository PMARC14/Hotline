using Hotline.Core.Windowing;
using Xunit;

namespace Hotline.Core.Tests;

public class PanelDisplayRecoveryTests
{
    [Fact]
    public void Recovery_preserves_actual_size_and_scales_cached_height_once()
    {
        var work = new RectI(0, 0, 1920, 1040);
        var actual = new RectI(100, 200, 900, 600);
        var r = PanelDisplayRecovery.Calculate(work, work, 1, 1.5, actual, new RectI(200, 700, 600, 60), 400, true);
        Assert.Equal(actual, r.Rect);
        Assert.Equal(600, r.FixedHeight);
        Assert.Equal(new RectI(250, 710, 600, 90), r.Bar);
        var again = PanelDisplayRecovery.Calculate(work, work, 1.5, 1.5, r.Rect, r.Bar, r.FixedHeight, true);
        Assert.Equal(r, again);
    }

    [Fact]
    public void Removed_monitor_clamps_actual_rect_into_surviving_work_area()
    {
        var r = PanelDisplayRecovery.Calculate(new RectI(1920, 0, 1920, 1040), new RectI(-1280, 40, 1280, 680),
            1.5, 1, new RectI(2200, 500, 1500, 900), new RectI(2300, 1300, 600, 90), 900, false);
        Assert.Equal(new RectI(-1280, 40, 1280, 680), r.Rect);
        Assert.Equal(new RectI(-940, 40, 600, 60), r.Bar);
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
}
