using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class RelativeWidthTests
{
    [Theory]
    [InlineData(2880, 2.0, 40, 600, 1000, 600)]   // 1440 DIP wide screen → 576 → clamped up to 600
    [InlineData(3840, 1.0, 40, 600, 1000, 1000)]  // 3840 DIP → 1536 → clamped down to 1000
    [InlineData(2560, 1.0, 30, 600, 1000, 768)]   // 30% of 2560
    [InlineData(1920, 1.5, 50, 600, 1000, 640)]   // 1280 DIP * 50%
    public void Width_is_percent_of_work_area_in_dip_clamped(int workPx, double scale, double pct, int min, int max, int expected)
        => Assert.Equal(expected, PopupGeometry.RelativeWidthDip(workPx, scale, pct, min, max));

    [Fact]
    public void Same_fraction_across_scales()
        => Assert.Equal(PopupGeometry.RelativeWidthDip(3840, 2.0, 40, 100, 5000), PopupGeometry.RelativeWidthDip(1920, 1.0, 40, 100, 5000));

    [Fact]
    public void Max_below_min_uses_min()
        => Assert.Equal(700, PopupGeometry.RelativeWidthDip(1000, 1.0, 10, 700, 500));
}
