using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class ToolbarFitTests
{
    private const double Gap = 4;

    // Mirror of the documented rule: items-1 gaps across [left..., overflow?, right...], plus one before reserved.
    private static double Total(FitResult r, double[] left, double[] right, double reserved, double ov = 32)
    {
        var items = r.LeftShown + right.Length - r.RightDropped + (r.Overflow ? 1 : 0);
        var gaps = Math.Max(0, items - 1) + (reserved > 0 && items > 0 ? 1 : 0);
        return right.Skip(r.RightDropped).Sum() + left.Take(r.LeftShown).Sum() + (r.Overflow ? ov : 0) + Gap * gaps + reserved;
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
        // Changed: the right group used to stay whole (overshooting); now it gives up its first buttons into the menu, keeping one.
        Assert.Equal(new FitResult(0, true, 1), ToolbarLayout.Fit(50, Gap, [30, 30], [30, 30], 0));
        Assert.Equal(new FitResult(0, true, 1), ToolbarLayout.Fit(50, Gap, [], [30, 30], 0));
        Assert.Equal(new FitResult(0, false), ToolbarLayout.Fit(64, Gap, [], [30, 30], 0)); // exactly fitting: unchanged
        // Not even one right button and the "..." button: the documented overshoot.
        Assert.False(ToolbarLayout.FitChecked(50, Gap, [], [30, 30], 0).Fits);
        Assert.True(ToolbarLayout.FitChecked(66, Gap, [], [30, 30], 0).Fits);
    }

    [Fact]
    public void The_right_group_gives_up_buttons_from_its_left_end_one_at_a_time()
    {
        double[] right = [30, 30, 30, 30];
        // Whole: 4 * 30 + 3 gaps = 132. Every dropped button needs the "..." button: 3 right = 90 + 32 + 3 gaps = 134.
        Assert.Equal(new FitResult(0, false), ToolbarLayout.Fit(132, Gap, [], right, 0));
        Assert.Equal(new FitResult(0, true, 2), ToolbarLayout.Fit(131, Gap, [], right, 0)); // 2 right = 60 + 32 + 2 gaps = 100
        Assert.Equal(new FitResult(0, true, 2), ToolbarLayout.Fit(100, Gap, [], right, 0));
        Assert.Equal(new FitResult(0, true, 3), ToolbarLayout.Fit(99, Gap, [], right, 0)); // 1 right = 30 + 32 + 1 gap = 66
        Assert.Equal(new FitResult(0, true, 3), ToolbarLayout.Fit(66, Gap, [], right, 0));
        Assert.Equal(new FitResult(0, true, 3), ToolbarLayout.Fit(10, Gap, [], right, 0)); // overshoot, one button kept
        // The left group always goes first: with it the right group stays whole as long as the "..." button fits beside it.
        Assert.Equal(new FitResult(0, true), ToolbarLayout.Fit(168, Gap, [30, 30], right, 0));
        Assert.Equal(new FitResult(0, true, 1), ToolbarLayout.Fit(167, Gap, [30, 30], right, 0));
        // The pickers' reserve counts before the right group keeps a button: 2 right + "..." + 60 + 3 gaps = 164.
        Assert.Equal(new FitResult(0, true, 2), ToolbarLayout.Fit(164, Gap, [], right, 60));
        Assert.Equal(new FitResult(0, true, 3), ToolbarLayout.Fit(163, Gap, [], right, 60));
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
    public void The_right_group_gives_up_a_button_when_it_fits_but_the_overflow_button_does_not()
    {
        // right alone = 60 + 4 = 64 <= 70; with the button 60 + 32 + 8 = 100 > 70 -> one right button moves into the menu: 30 + 32 + 4 = 66
        // (this was the old overshoot case, (0, true) at 100).
        double[] left = [30], right = [30, 30];
        var r = ToolbarLayout.Fit(70, Gap, left, right, 0);
        Assert.Equal(new FitResult(0, true, 1), r);
        Assert.Equal(66, Total(r, left, right, 0));
    }

    [Fact]
    public void Dropping_the_pickers_can_keep_more_buttons()
    {
        double[] left = [30], right = [30, 30];
        var with = ToolbarLayout.Fit(100, Gap, left, right, 10); // right+reserved = 78 fits, the button version 114 does not: one right button goes
        Assert.Equal(new FitResult(0, true, 1), with);
        Assert.True(Total(with, left, right, 10) <= 100);
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
                double[] right = [size, size, size];
                var reserved = rng.Next(2) == 0 ? 0 : 90.0;
                var (prev, prevDropped) = (-1, right.Length);
                for (double room = 100; room <= 1600; room += 8)
                {
                    var r = ToolbarLayout.Fit(room, Gap, left, right, reserved);
                    Assert.Equal(r, ToolbarLayout.Fit(room, Gap, left, right, reserved));
                    Assert.InRange(r.LeftShown, 0, count);
                    Assert.Equal(r.LeftShown < count || r.RightDropped > 0, r.Overflow);
                    Assert.True(r.LeftShown >= prev, $"not monotonic at {room}");
                    // The right group gives up buttons only when no left button is left, and never its last one.
                    Assert.True(r.RightDropped == 0 || r.LeftShown == 0, $"right dropped with left shown at {room}");
                    Assert.InRange(r.RightDropped, 0, right.Length - 1);
                    Assert.True(r.RightDropped <= prevDropped, $"more room dropped more at {room}");
                    (prev, prevDropped) = (r.LeftShown, r.RightDropped);
                    // Within the room, except when even one right button and the "..." button (and the reserve) do not fit.
                    var total = Total(r, left, right, reserved);
                    var smallest = Total(new FitResult(0, true, right.Length - 1), left, right, reserved);
                    if (smallest <= room) Assert.True(total <= room, $"overflows room {room} size {size} n {count}");
                    else Assert.Equal(new FitResult(0, true, right.Length - 1), r);
                    Assert.Equal(smallest <= room || total <= room, ToolbarLayout.FitChecked(room, Gap, left, right, reserved).Fits);
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
