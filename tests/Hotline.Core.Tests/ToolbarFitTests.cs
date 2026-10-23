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

    // Left [30,30,30] or [30,30,60] with right [30,30]: the left group gives up buttons from its end, then the pickers' reserve counts too.
    // 3 left (30 each) + 2 right (30 each): 150 + 4 gaps*4 = 166. With the 60 button: 196. The "..." button is 32 wide.
    [Theory]
    [InlineData(false, 0, 166, 3, false)] // everything fits
    [InlineData(true, 0, 196, 3, false)]
    [InlineData(true, 0, 195, 2, true)] // one dropped
    [InlineData(true, 0, 168, 2, true)] // 120 + 32 + 4*4
    [InlineData(true, 0, 167, 1, true)]
    [InlineData(false, 0, 100, 0, true)] // all left dropped: overflow + 2 right = 60 + 32 + 2*4
    [InlineData(false, 0, 133, 0, true)] // one left would need 100 + 30 + 4 = 134
    [InlineData(false, 0, 134, 1, true)]
    [InlineData(true, 60, 260, 3, false)] // reserved width shrinks the room: 196 + 60 + 4 (gap before it)
    [InlineData(true, 60, 259, 2, true)]
    [InlineData(true, 60, 232, 2, true)] // 168 + 64
    [InlineData(true, 60, 231, 1, true)]
    public void The_left_group_gives_up_buttons_from_its_end(bool bigLast, double reserved, double room, int shown, bool overflow)
    {
        double[] left = bigLast ? [30, 30, 60] : [30, 30, 30];
        Assert.Equal(new FitResult(shown, overflow), ToolbarLayout.Fit(room, Gap, left, [30, 30], reserved));
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
    public void Overflow_width_counted_only_when_dropping()
    {
        Assert.Equal(new FitResult(2, false), ToolbarLayout.Fit(100, Gap, [30, 30], [], 0, overflowWidth: 500)); // fits whole: button width irrelevant
        Assert.Equal(new FitResult(0, true), ToolbarLayout.Fit(40, Gap, [30, 30], [], 0, overflowWidth: 40));
        Assert.Equal(new FitResult(1, true), ToolbarLayout.Fit(70, Gap, [30, 40], [], 0, overflowWidth: 32)); // 70 < 74 whole; 30+32+4 = 66 with button
        // An empty group on either side.
        Assert.Equal(new FitResult(0, false), ToolbarLayout.Fit(100, Gap, [], [30], 0));
        Assert.Equal(new FitResult(2, false), ToolbarLayout.Fit(64, Gap, [30, 30], [], 0));
        Assert.Equal(new FitResult(0, false), ToolbarLayout.Fit(0, Gap, [], [], 0));
    }

    [Fact]
    public void Bad_inputs_never_throw()
    {
        var r = ToolbarLayout.Fit(double.NaN, -1, [double.NaN, -5], [-1], double.NaN, double.NaN);
        Assert.Equal(new FitResult(2, false), r);
        Assert.Equal(new FitResult(2, false), ToolbarLayout.Fit(100, double.PositiveInfinity, [30, 30], [], 0)); // infinite spacing: no NaN
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
}
