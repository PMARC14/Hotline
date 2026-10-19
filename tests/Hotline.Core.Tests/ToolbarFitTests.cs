using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class ToolbarFitTests
{
    private const double Gap = 4;

    // Mirror of the documented rule: items-1 gaps across [left..., overflow?, right...], plus one before reserved.
    private static double Total(FitResult r, double[] left, double[] right, double reserved, double ov = 32)
    {
        var items = r.LeftShown + right.Length + (r.Overflow ? 1 : 0);
        var gaps = Math.Max(0, items - 1) + (reserved > 0 && items > 0 ? 1 : 0);
        return right.Sum() + left.Take(r.LeftShown).Sum() + (r.Overflow ? ov : 0) + Gap * gaps + reserved;
    }

    [Fact]
    public void Everything_fits()
    {
        // 3 left (30 each) + 2 right (30 each): 150 + 4 gaps*4 = 166
        Assert.Equal(new FitResult(3, false), ToolbarLayout.Fit(166, Gap, [30, 30, 30], [30, 30], 0));
    }

    [Fact]
    public void One_dropped()
    {
        // left [30,30,60]: all fit = 180 + 4*4 + 0 = 196 (5 items); 2 + overflow = 60+30+... 120+32 + 4*4 = 168
        Assert.Equal(new FitResult(3, false), ToolbarLayout.Fit(196, Gap, [30, 30, 60], [30, 30], 0));
        Assert.Equal(new FitResult(2, true), ToolbarLayout.Fit(195, Gap, [30, 30, 60], [30, 30], 0));
        Assert.Equal(new FitResult(2, true), ToolbarLayout.Fit(168, Gap, [30, 30, 60], [30, 30], 0));
        Assert.Equal(new FitResult(1, true), ToolbarLayout.Fit(167, Gap, [30, 30, 60], [30, 30], 0));
    }

    [Fact]
    public void All_left_dropped()
    {
        // overflow + 2 right: 60 + 32 + 2*4 = 100; one left would need 100 + 30 + 4 = 134
        Assert.Equal(new FitResult(0, true), ToolbarLayout.Fit(100, Gap, [30, 30, 30], [30, 30], 0));
        Assert.Equal(new FitResult(0, true), ToolbarLayout.Fit(133, Gap, [30, 30, 30], [30, 30], 0));
        Assert.Equal(new FitResult(1, true), ToolbarLayout.Fit(134, Gap, [30, 30, 30], [30, 30], 0));
    }

    [Fact]
    public void Right_alone_too_wide()
    {
        Assert.Equal(new FitResult(0, true), ToolbarLayout.Fit(50, Gap, [30, 30], [30, 30], 0));
        Assert.Equal(new FitResult(0, false), ToolbarLayout.Fit(50, Gap, [], [30, 30], 0));
    }

    [Fact]
    public void Reserved_width_shrinks_the_room()
    {
        // left [30,30,60]: all fit with reserved 60 = 196 + 60 + 4 (gap before reserved) = 260; 2 + overflow = 168 + 64 = 232
        Assert.Equal(new FitResult(3, false), ToolbarLayout.Fit(260, Gap, [30, 30, 60], [30, 30], 60));
        Assert.Equal(new FitResult(2, true), ToolbarLayout.Fit(259, Gap, [30, 30, 60], [30, 30], 60));
        Assert.Equal(new FitResult(2, true), ToolbarLayout.Fit(232, Gap, [30, 30, 60], [30, 30], 60));
        Assert.Equal(new FitResult(1, true), ToolbarLayout.Fit(231, Gap, [30, 30, 60], [30, 30], 60));
    }

    [Fact]
    public void Overflow_width_counted_only_when_dropping()
    {
        Assert.Equal(new FitResult(2, false), ToolbarLayout.Fit(100, Gap, [30, 30], [], 0, overflowWidth: 500)); // fits whole: button width irrelevant
        Assert.Equal(new FitResult(0, true), ToolbarLayout.Fit(40, Gap, [30, 30], [], 0, overflowWidth: 40));
        Assert.Equal(new FitResult(1, true), ToolbarLayout.Fit(70, Gap, [30, 40], [], 0, overflowWidth: 32)); // 70 < 74 whole; 30+32+4 = 66 with button
    }

    [Fact]
    public void Empty_left_and_empty_right()
    {
        Assert.Equal(new FitResult(0, false), ToolbarLayout.Fit(100, Gap, [], [30], 0));
        Assert.Equal(new FitResult(2, false), ToolbarLayout.Fit(64, Gap, [30, 30], [], 0));
        Assert.Equal(new FitResult(0, false), ToolbarLayout.Fit(0, Gap, [], [], 0));
    }

    [Fact]
    public void Bad_inputs_never_throw()
    {
        var r = ToolbarLayout.Fit(double.NaN, -1, [double.NaN, -5], [-1], double.NaN, double.NaN);
        Assert.Equal(new FitResult(2, false), r);
    }

    [Fact]
    public void Overshoot_when_right_fits_but_button_does_not()
    {
        // right alone = 60 + 4 = 64 <= 70; with the button 60 + 32 + 8 = 100 > 70 -> (0, true), over by 30 <= 32 + 4
        double[] left = [30], right = [30, 30];
        var r = ToolbarLayout.Fit(70, Gap, left, right, 0);
        Assert.Equal(new FitResult(0, true), r);
        Assert.Equal(100, Total(r, left, right, 0));
    }

    [Fact]
    public void Dropping_the_pickers_can_remove_the_overshoot()
    {
        double[] left = [30], right = [30, 30];
        var with = ToolbarLayout.Fit(100, Gap, left, right, 10); // right+reserved = 78 fits, button version 114 does not
        Assert.Equal(new FitResult(0, true), with);
        Assert.True(Total(with, left, right, 10) > 100);
        var without = ToolbarLayout.Fit(100, Gap, left, right, 0);
        Assert.Equal(new FitResult(1, false), without);
        Assert.True(Total(without, left, right, 0) <= 100);
    }

    [Fact]
    public void Infinite_spacing_does_not_produce_nan()
    {
        Assert.Equal(new FitResult(2, false), ToolbarLayout.Fit(100, double.PositiveInfinity, [30, 30], [], 0));
    }

    [Fact]
    public void Property_over_widths_presets_and_button_counts()
    {
        var rng = new Random(26);
        foreach (var size in new double[] { 30, 36, 46 })
        {
            for (var count = 0; count <= 24; count++)
            {
                var left = Enumerable.Range(0, count).Select(_ => size).ToArray();
                double[] right = [size, size];
                var reserved = rng.Next(2) == 0 ? 0 : 90.0;
                var prev = -1;
                for (double room = 200; room <= 1600; room += 8)
                {
                    var r = ToolbarLayout.Fit(room, Gap, left, right, reserved);
                    Assert.Equal(r, ToolbarLayout.Fit(room, Gap, left, right, reserved));
                    Assert.InRange(r.LeftShown, 0, count);
                    Assert.Equal(r.LeftShown < count, r.Overflow);
                    Assert.True(r.LeftShown >= prev, $"not monotonic at {room}");
                    prev = r.LeftShown;
                    var total = Total(r, left, right, reserved);
                    var floor = Total(new FitResult(0, count > 0), left, right, reserved);
                    if (floor <= room) Assert.True(total <= room, $"overflows room {room} size {size} n {count}");
                    // Only overshoot allowed: (0, true) when right + reserved fits but the button does not.
                    var rightOnly = Total(new FitResult(0, false), left, right, reserved);
                    if (total > room && rightOnly <= room)
                    {
                        Assert.Equal(new FitResult(0, true), r);
                        Assert.True(total <= room + 32 + Gap, $"overshoot too large at {room}");
                    }
                    if (reserved == 0 && rightOnly <= room && !(r.LeftShown == 0 && r.Overflow))
                        Assert.True(total <= room, $"overflows room {room} size {size} n {count}");
                }
            }
        }
    }

    [Fact]
    public void Review_focus_room_320_with_24_buttons_fits()
    {
        var left = Enumerable.Repeat(36.0, 24).ToArray();
        double[] right = [36, 36];
        var r = ToolbarLayout.Fit(320, Gap, left, right, 0);
        Assert.True(r.Overflow);
        Assert.True(Total(r, left, right, 0) <= 320);
    }
}
