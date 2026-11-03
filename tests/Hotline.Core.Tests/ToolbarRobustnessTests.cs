using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>
/// The button rows under stress: seeded random bars (every size preset, custom buttons at their limits, left, centre
/// and right groups, with and without the pickers) fitted to every panel width the way <c>ToolbarRenderer.Fit</c> does.
/// Whatever the bar, a fit never throws, sizes stay in range, the row stays in the room (or overshoots only as
/// documented), more room never hides more, and what goes into the "⋯" menu keeps its order.
/// </summary>
public sealed class ToolbarRobustnessTests
{
    private const double Spacing = ToolbarPlan.CellGap; // the rows have no column gap: each cell holds its own padding
    private const double PickersMin = 150;
    // A button is half a cell to four cells wide and always exactly one cell high; its glyph 0.8 to 1 times the base's.
    private static readonly double MinWidth = 0.5 * ToolbarPlan.BaseWidth, MaxWidth = 4 * ToolbarPlan.BaseWidth;
    private static readonly double MinFont = Math.Round(0.8 * ToolbarPlan.BaseFont), MaxFont = ToolbarPlan.BaseFont;

    private static readonly string[] Buttons = [.. ToolbarModel.BuiltIn.Where(id => !ToolbarPlan.Stays(id))]; // the more button has its own tests (ToolbarMoreMenuTests)
    private static readonly string[] Pickers = ["effort", "model", "provider"];

    /// <summary>One row fitted as the renderer does it: widths from <see cref="ToolbarPlan.Metrics(ToolbarModel, string)"/>
    /// (left then centre can drop, the pickers excluded), the "⋯" button as wide as the bar's preset.</summary>
    private sealed record Fitted(RowPlan Plan, double[] Left, double[] Right, bool HasPickers, double Overflow, RowFit Fit)
    {
        public double Used => Fit.FixedWidth + (Fit.PickersKept ? PickersMin : 0);

        /// <summary>The row with no left button: the right group and the gaps of every column but the left ones.</summary>
        public double RightOnly =>
            Right.Sum() + Spacing * (Right.Length + (HasPickers ? 1 : 0) + (Plan.Center.Count > 0 ? 1 : 0));

        /// <summary>The width of the row (pickers excluded) with <paramref name="leftShown"/> left buttons and the right group's
        /// first <paramref name="rightDropped"/> in the menu: every shown button, "⋯", and the gaps between all columns.</summary>
        public double Width(int leftShown, int rightDropped)
        {
            var overflow = leftShown < Left.Length || rightDropped > 0;
            var columns = leftShown + Right.Length - rightDropped + (overflow ? 1 : 0) + (HasPickers ? 1 : 0) + (Plan.Center.Count > 0 ? 2 : 1);
            return Left.Take(leftShown).Sum() + Right.Skip(rightDropped).Sum() + (overflow ? Overflow : 0) + Spacing * (columns - 1);
        }

        /// <summary>Buttons on the bar (not in the menu): never fewer with more room while the pickers keep the same state.</summary>
        public int ButtonsShown => Fit.LeftShown + Right.Length - Fit.RightDropped;
    }

    private static Fitted FitRow(ToolbarModel m, bool top, double room, double pickersMin = PickersMin)
    {
        var plan = ToolbarPlan.Row(m, top);
        var left = plan.Droppable.Where(id => !ToolbarPlan.IsPicker(id)).Select(id => ToolbarPlan.Metrics(m, id).Width).ToArray();
        var right = plan.Right.Where(id => !ToolbarPlan.IsPicker(id)).Select(id => ToolbarPlan.Metrics(m, id).Width).ToArray();
        var hasPickers = plan.Droppable.Concat(plan.Right).Any(ToolbarPlan.IsPicker);
        var overflow = ToolbarPlan.BaseWidth; // the automatic "..." button is one cell whatever the bar size
        var fit = ToolbarPlan.FitRow(room, Spacing, left, right, hasPickers, pickersMin, overflow, hasCenter: plan.Center.Count > 0);
        return new Fitted(plan, left, right, hasPickers, overflow, fit);
    }

    private static ButtonStyle RandomStyle(Random r, bool bar) => new(
        // In memory: Metrics must ignore a width Resolve would have dropped.
        r.Next(4) == 0 && !bar ? null : new[] { 0.5, 1, 2, 3, 4, 0.5, 1, 2, 0, 5, 1.3, -2 }[r.Next(12)],
        r.Next(3) == 0 ? null : r.Next(2) == 0,
        r.Next(3) == 0 ? null : r.Next(2) == 0,
        r.Next(3) == 0 ? "#E81123" : null);

    /// <summary>A random bar built in memory (as Settings and the self-test do): up to 24 custom buttons, every built-in
    /// button and the pickers spread over the six groups, random looks for the bar and the items.</summary>
    private static ToolbarModel RandomModel(Random r)
    {
        var custom = Enumerable.Range(1, r.Next(0, ToolbarModel.MaxButtons + 1))
            .Select(i => new CustomButton($"c{i}", $"Custom {i}", "E734", null, "t", false)).ToList();
        var placed = Enum.GetValues<Quadrant>().ToDictionary(q => q, _ => new List<string>());
        var quadrants = Enum.GetValues<Quadrant>();
        foreach (var id in Buttons.Concat(custom.Select(c => c.Id)))
            if (r.Next(6) > 0) placed[quadrants[r.Next(quadrants.Length)]].Add(id);
        if (r.Next(3) > 0) placed[quadrants[r.Next(quadrants.Length)]].AddRange(Pickers.Take(r.Next(1, 4)));
        foreach (var list in placed.Values) Shuffle(r, list);
        var overrides = new Dictionary<string, ButtonStyle>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in placed.Values.SelectMany(l => l).Where(id => !ToolbarPlan.IsPicker(id)))
            if (r.Next(3) == 0) overrides[id] = RandomStyle(r, bar: false);
        return ToolbarModel.Defaults with
        {
            Placed = placed.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value),
            Style = RandomStyle(r, bar: true),
            Overrides = overrides,
            Buttons = custom,
        };
    }

    private static void Shuffle(Random r, List<string> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = r.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static void AssertMetricsInRange(ToolbarModel m)
    {
        foreach (var id in Enum.GetValues<Quadrant>().SelectMany(m.Items).Where(id => !ToolbarPlan.IsPicker(id)))
        {
            var size = ToolbarPlan.Metrics(m, id);
            Assert.True(double.IsFinite(size.Width) && double.IsFinite(size.Height) && double.IsFinite(size.FontSize), $"{id}: {size}");
            Assert.InRange(size.Width, MinWidth, MaxWidth);
            Assert.Equal(ToolbarPlan.BaseHeight, size.Height); // every row is one cell high
            Assert.InRange(size.FontSize, MinFont, MaxFont);
        }
    }

    /// <summary>Everything a single fit must hold.</summary>
    private static void AssertFit(Fitted f, double room, string where)
    {
        var fit = f.Fit;
        Assert.InRange(fit.LeftShown, 0, f.Left.Length);
        Assert.InRange(fit.RightDropped, 0, Math.Max(0, f.Right.Length - 1)); // never the last right button
        Assert.Equal(fit.LeftShown < f.Left.Length || fit.RightDropped > 0, fit.Overflow);
        Assert.True(fit.RightDropped == 0 || fit.LeftShown == 0, $"{where}: the right group gave up buttons before the left was gone ({fit})");
        Assert.True(double.IsFinite(fit.FixedWidth) && fit.FixedWidth >= 0, $"{where}: {fit}");
        Assert.Equal(f.Width(fit.LeftShown, fit.RightDropped), fit.FixedWidth, 6);
        // The narrowest the row can be: no left button, all but the last right button in the menu (the "⋯" button if anything is).
        var smallest = f.Width(0, Math.Max(0, f.Right.Length - 1));
        // Two compact right buttons can fit where replacing one with the wider menu button would not.
        if (Math.Min(f.Width(0, 0), smallest) <= room)
        {
            Assert.True(f.Used <= room + 1e-9, $"{where}: uses {f.Used} of {room} ({fit})");
            // The right group gave up no more than it had to: one more button back would not fit (with the pickers' room if kept).
            if (fit.RightDropped > 0)
                Assert.True(f.Width(0, fit.RightDropped - 1) + (fit.PickersKept ? PickersMin : 0) > room, $"{where}: {fit} gave up too many");
        }
        else
        {
            // Fit's documented overshoot, and the only one: not even one right button and the "⋯" button fit, with the pickers given up.
            Assert.Equal(new RowFit(0, f.Left.Length > 0 || f.Right.Length > 1, false, smallest, Math.Max(0, f.Right.Length - 1)), fit);
        }

        // What the renderer puts into the "⋯" menu: the end of left-then-centre, in order, the centre before any left button;
        // then the right group's first buttons, in order.
        var (rightShown, rightDropped) = ToolbarPlan.SplitRight(f.Plan.Right, fit.RightDropped);
        Assert.Equal(f.Plan.Right.Where(id => !ToolbarPlan.IsPicker(id)).Take(fit.RightDropped), rightDropped);
        Assert.Equal(f.Plan.Right, rightShown.Concat(rightDropped).OrderBy(id => f.Plan.Right.ToList().IndexOf(id)));
        var (shown, dropped) = ToolbarPlan.Split(f.Plan.Droppable, fit.LeftShown);
        Assert.Equal(f.Plan.Droppable.Where(id => !ToolbarPlan.IsPicker(id)).Skip(fit.LeftShown), dropped);
        Assert.Equal(f.Plan.Droppable, shown.Concat(dropped).OrderBy(id => f.Plan.Droppable.ToList().IndexOf(id)));
        if (dropped.Any(f.Plan.Left.Contains)) Assert.All(f.Plan.Center.Where(id => !ToolbarPlan.IsPicker(id)), id => Assert.Contains(id, dropped));
    }

    [Fact]
    public void Random_bars_fit_every_width_from_320_to_2000()
    {
        var r = new Random(20261021);
        var (overshoots, rightTooWide, keptAndDropped, centres, rightDropped) = (0, 0, 0, 0, 0);
        for (var run = 0; run < 400; run++)
        {
            var m = RandomModel(r);
            AssertMetricsInRange(m);
            foreach (var top in new[] { true, false })
            {
                Fitted? previous = null;
                for (double room = 320; room <= 2000; room += r.Next(1, 24))
                {
                    var f = FitRow(m, top, room);
                    var where = $"run {run} top {top} room {room}";
                    AssertFit(f, room, where);
                    Assert.Equal(f.Fit, FitRow(m, top, room).Fit); // repeatable
                    if (previous is { } p)
                    {
                        // More room never takes the pickers' room away, and never hides a button while that stays the same. "A button"
                        // is now any button on the bar, left or right: the left ones go first, then the right group's first ones, so
                        // LeftShown plus the right buttons still shown never falls (it was LeftShown alone while the right group stayed whole).
                        Assert.True(f.Fit.PickersKept || !p.Fit.PickersKept, $"{where}: pickers lost their room");
                        if (f.Fit.PickersKept == p.Fit.PickersKept)
                            Assert.True(f.ButtonsShown >= p.ButtonsShown, $"{where}: {p.ButtonsShown} -> {f.ButtonsShown}");
                        if (!f.HasPickers) Assert.True(f.ButtonsShown >= p.ButtonsShown, $"{where}: not monotone");
                    }
                    previous = f;
                    if (f.Used > room) overshoots++;
                    if (f.RightOnly > room) rightTooWide++;
                    if (f.Fit.RightDropped > 0) rightDropped++;
                    if (f.Fit.PickersKept && f.Fit.Overflow) keptAndDropped++;
                    if (f.Plan.Center.Count > 0 && f.Fit.Overflow) centres++;
                }
            }
        }
        // The sweep reaches the interesting cases, not only bars that always fit.
        // (Changed: no row overshoots from 320 up any more; the overshoot is only for rooms under about two buttons, see below.)
        Assert.Equal(0, overshoots);
        Assert.True(rightTooWide > 0 && keptAndDropped > 0 && centres > 0 && rightDropped > 0,
            $"right too wide {rightTooWide}, kept and dropped {keptAndDropped}, centre dropped {centres}, right dropped {rightDropped}");
    }

    [Fact]
    public void Panels_narrower_than_320_overshoot_only_when_one_right_button_and_the_menu_button_do_not_fit()
    {
        var r = new Random(99);
        var overshoots = 0;
        for (var run = 0; run < 200; run++)
        {
            var m = RandomModel(r);
            foreach (var top in new[] { true, false })
                for (double room = 0; room < 320; room += 5)
                {
                    var f = FitRow(m, top, room);
                    AssertFit(f, room, $"run {run} top {top} room {room}"); // checks the exact overshoot shape
                    if (f.Used > room) overshoots++;
                }
        }
        Assert.True(overshoots > 0, "the sweep reaches the overshoot");
    }

    [Fact]
    public void Every_width_value_is_exactly_one_cell_high_for_built_in_and_custom_buttons()
    {
        foreach (var width in new double?[] { null, 0.5, 1, 2, 3, 4, 0, 5, 1.3, double.NaN })
        {
            var style = new ButtonStyle(width);
            var m = ToolbarPlan.Metrics(style);
            Assert.Equal(ToolbarPlan.BaseHeight, m.Height);
            Assert.InRange(m.Width, MinWidth, MaxWidth);
            Assert.Equal(ToolbarPlan.BaseHeight, ToolbarPlan.Metrics(style, 70, 36, 30).Height);
        }
    }

    [Fact]
    public void Resolve_of_24_custom_buttons_at_their_widest_on_a_wide_bar_stays_within_limits()
    {
        var buttons = string.Join(", ", Enumerable.Range(1, 24).Select(i => $$""" "c{{i}}": { "label": "C{{i}}", "text": "t" } """));
        var overrides = string.Join(", ", Enumerable.Range(1, 24).Select(i => $$""" "c{{i}}": { "width": 4, "height": 80 } """));
        var ids = string.Join(", ", Enumerable.Range(1, 24).Select(i => $"\"c{i}\""));
        var m = ToolbarModel.Resolve($$"""
            { "version": 2, "top": { "left": ["effort", "model", "provider"], "right": [] }, "items": { "left": [{{ids}}], "right": ["settings"] },
              "style": { "width": "wide", "cells": 999 }, "overrides": { {{overrides}} }, "buttons": { {{buttons}} } }
            """);
        Assert.Equal(24, m.Buttons.Count);
        AssertMetricsInRange(m);
        var widest = new ButtonMetrics(4 * ToolbarPlan.BaseWidth, ToolbarPlan.BaseHeight, ToolbarPlan.BaseFont);
        Assert.All(m.Buttons, b => Assert.Equal(widest, ToolbarPlan.Metrics(m, b.Id)));
        // The bar's old cells key is ignored next to its width: wide applies; the height stays one cell.
        Assert.Equal(new ButtonMetrics(2 * ToolbarPlan.BaseWidth, ToolbarPlan.BaseHeight, ToolbarPlan.BaseFont), ToolbarPlan.Metrics(m, "settings"));
    }

    [Fact]
    public void A_right_group_or_a_single_button_wider_than_the_room_never_throws_and_shows_no_left_button()
    {
        double[] big = [108, 108, 108, 108]; // e.g. a base grown past today's
        foreach (var hasPickers in new[] { true, false })
            foreach (var hasCenter in new[] { true, false })
                for (double room = -10; room <= 500; room += 3)
                {
                    // The right group gives up its first buttons (it used to stay whole), never its last: at least one stays.
                    var fit = ToolbarPlan.FitRow(room, Spacing, [36, 108], big, hasPickers, PickersMin, 108, hasCenter);
                    Assert.True(fit.LeftShown == 0 && fit.Overflow && fit.RightDropped is >= 0 and <= 3, $"{room}: {fit}");
                    var single = ToolbarPlan.FitRow(room, Spacing, [108], [], hasPickers, PickersMin, 108, hasCenter);
                    Assert.True(double.IsFinite(single.FixedWidth));
                    if (room < 108) Assert.Equal(0, single.LeftShown);
                }
        // Bad numbers from a broken measure: still a fit.
        var bad = ToolbarPlan.FitRow(double.NaN, double.PositiveInfinity, [double.NaN, -1, 1e308], [double.PositiveInfinity], true, double.NaN, double.NaN, true);
        Assert.InRange(bad.LeftShown, 0, 3);
    }
}
