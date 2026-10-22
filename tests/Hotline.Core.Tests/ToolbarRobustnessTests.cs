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
    private const double Spacing = 2; // the rows' Grid.ColumnSpacing
    private const double PickersMin = 150;
    private const int MaxCustom = 80, MinDip = 20;
    // A built-in button is at most wide (twice the base) or an explicit 80; its glyph 0.8 to 1.2 times the base's.
    private static readonly double MaxBuiltInWidth = Math.Max(80, 2 * ToolbarPlan.BaseWidth), MaxBuiltInHeight = Math.Max(80, 2 * ToolbarPlan.BaseHeight);
    private static readonly double MinFont = Math.Round(0.8 * ToolbarPlan.BaseFont), MaxFont = Math.Round(1.2 * ToolbarPlan.BaseFont);

    private static readonly string[] Buttons = [.. ToolbarModel.BuiltIn.Where(id => !ToolbarPlan.IsPicker(id))];
    private static readonly string[] Pickers = ["effort", "model", "provider"];

    /// <summary>One row fitted as the renderer does it: widths from <see cref="ToolbarPlan.Metrics(ToolbarModel, string)"/>
    /// (left then centre can drop, the pickers excluded), the "⋯" button as wide as the bar's preset.</summary>
    private sealed record Fitted(RowPlan Plan, double[] Left, double[] Right, bool HasPickers, double Overflow, RowFit Fit)
    {
        public double Used => Fit.FixedWidth + (Fit.PickersKept ? PickersMin : 0);

        /// <summary>The row with no left button: the right group and the gaps of every column but the left ones.</summary>
        public double RightOnly =>
            Right.Sum() + Spacing * (Right.Length + (HasPickers ? 1 : 0) + (Plan.Center.Count > 0 ? 1 : 0));
    }

    private static Fitted FitRow(ToolbarModel m, bool top, double room, double pickersMin = PickersMin)
    {
        var plan = ToolbarPlan.Row(m, top);
        var left = plan.Droppable.Where(id => !ToolbarPlan.IsPicker(id)).Select(id => ToolbarPlan.Metrics(m, id).Width).ToArray();
        var right = plan.Right.Where(id => !ToolbarPlan.IsPicker(id)).Select(id => ToolbarPlan.Metrics(m, id).Width).ToArray();
        var hasPickers = plan.Droppable.Concat(plan.Right).Any(ToolbarPlan.IsPicker);
        var overflow = ToolbarPlan.Metrics(m.Style).Width;
        var fit = ToolbarPlan.FitRow(room, Spacing, left, right, hasPickers, pickersMin, overflow, hasCenter: plan.Center.Count > 0);
        return new Fitted(plan, left, right, hasPickers, overflow, fit);
    }

    private static ButtonStyle RandomStyle(Random r, bool bar) => new(
        r.Next(4) == 0 && !bar ? null : Enum.GetValues<ButtonSize>()[r.Next(Enum.GetValues<ButtonSize>().Length)],
        r.Next(3) == 0 ? null : r.Next(2) == 0,
        r.Next(3) == 0 ? null : r.Next(2) == 0,
        r.Next(3) == 0 ? r.Next(-40, 300) : null, // in memory: Metrics must clamp what Resolve would have
        r.Next(3) == 0 ? r.Next(-40, 300) : null,
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
            var custom = m.Buttons.Any(b => b.Id == id);
            Assert.True(double.IsFinite(size.Width) && double.IsFinite(size.Height) && double.IsFinite(size.FontSize), $"{id}: {size}");
            Assert.InRange(size.Width, MinDip, custom ? MaxCustom : MaxBuiltInWidth);
            Assert.InRange(size.Height, MinDip, custom ? MaxCustom : MaxBuiltInHeight);
            Assert.InRange(size.FontSize, Math.Min(MinFont, ToolbarPlan.MaxCustomFont), custom ? Math.Min(MaxFont, ToolbarPlan.MaxCustomFont) : MaxFont);
        }
    }

    /// <summary>Everything a single fit must hold.</summary>
    private static void AssertFit(Fitted f, double room, string where)
    {
        var fit = f.Fit;
        Assert.InRange(fit.LeftShown, 0, f.Left.Length);
        Assert.Equal(fit.LeftShown < f.Left.Length, fit.Overflow);
        Assert.True(double.IsFinite(fit.FixedWidth) && fit.FixedWidth >= 0, $"{where}: {fit}");
        // The narrowest the row can be: every left button without "⋯", or none of them with it.
        var smallest = f.Left.Length == 0 ? f.RightOnly
            : Math.Min(f.RightOnly + f.Left.Sum() + Spacing * f.Left.Length, f.RightOnly + f.Overflow + Spacing);
        if (smallest <= room)
            Assert.True(f.Used <= room + 1e-9, $"{where}: uses {f.Used} of {room} ({fit})");
        else if (f.RightOnly <= room)
        {
            // Fit's documented overshoot: only the "⋯" button (and its gap) sticks out, with the pickers given up.
            Assert.Equal(new RowFit(0, true, false, fit.FixedWidth), fit);
            Assert.True(f.Used <= room + f.Overflow + Spacing + 1e-9, $"{where}: overshoot {f.Used - room} at {room} ({fit})");
        }
        else Assert.Equal(0, fit.LeftShown); // the right group alone is wider than the room: it stays whole, nothing else shows

        // What the renderer puts into the "⋯" menu: the end of left-then-centre, in order, the centre before any left button.
        var (shown, dropped) = ToolbarPlan.Split(f.Plan.Droppable, fit.LeftShown);
        Assert.Equal(f.Plan.Droppable.Where(id => !ToolbarPlan.IsPicker(id)).Skip(fit.LeftShown), dropped);
        Assert.Equal(f.Plan.Droppable, shown.Concat(dropped).OrderBy(id => f.Plan.Droppable.ToList().IndexOf(id)));
        if (dropped.Any(f.Plan.Left.Contains)) Assert.All(f.Plan.Center.Where(id => !ToolbarPlan.IsPicker(id)), id => Assert.Contains(id, dropped));
    }

    [Fact]
    public void Random_bars_fit_every_width_from_320_to_2000()
    {
        var r = new Random(20261021);
        var (overshoots, rightTooWide, keptAndDropped, centres) = (0, 0, 0, 0);
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
                        // More room never takes the pickers' room away, and never hides a button while that stays the same.
                        Assert.True(f.Fit.PickersKept || !p.Fit.PickersKept, $"{where}: pickers lost their room");
                        if (f.Fit.PickersKept == p.Fit.PickersKept)
                            Assert.True(f.Fit.LeftShown >= p.Fit.LeftShown, $"{where}: {p.Fit.LeftShown} -> {f.Fit.LeftShown}");
                        if (!f.HasPickers) Assert.True(f.Fit.LeftShown >= p.Fit.LeftShown, $"{where}: not monotone");
                    }
                    previous = f;
                    if (f.Used > room) overshoots++;
                    if (f.RightOnly > room) rightTooWide++;
                    if (f.Fit.PickersKept && f.Fit.Overflow) keptAndDropped++;
                    if (f.Plan.Center.Count > 0 && f.Fit.Overflow) centres++;
                }
            }
        }
        // The sweep reaches the interesting cases, not only bars that always fit.
        Assert.True(overshoots > 0 && rightTooWide > 0 && keptAndDropped > 0 && centres > 0,
            $"overshoots {overshoots}, right too wide {rightTooWide}, kept and dropped {keptAndDropped}, centre dropped {centres}");
    }

    [Fact]
    public void Without_the_pickers_room_more_room_never_shows_fewer_buttons()
    {
        var r = new Random(7);
        for (var run = 0; run < 300; run++)
        {
            var m = RandomModel(r);
            foreach (var top in new[] { true, false })
            {
                var previous = -1;
                for (double room = 320; room <= 2000; room += 7)
                {
                    var f = FitRow(m, top, room, pickersMin: 0);
                    AssertFit(f, room, $"run {run} room {room}");
                    Assert.True(f.Fit.LeftShown >= previous, $"run {run} top {top} room {room}: {previous} -> {f.Fit.LeftShown}");
                    previous = f.Fit.LeftShown;
                }
            }
        }
    }

    [Theory]
    [InlineData(ButtonSize.Compact)]
    [InlineData(ButtonSize.Normal)]
    [InlineData(ButtonSize.Wide)]
    public void Every_preset_with_24_custom_buttons_in_every_group_fits_from_320(ButtonSize size)
    {
        var custom = Enumerable.Range(1, 24).Select(i => new CustomButton($"c{i}", $"C{i}", "E734", null, "t", false)).ToList();
        var ids = custom.Select(c => c.Id).Concat(Buttons).ToList();
        foreach (var withPickers in new[] { true, false })
        {
            var placed = new Dictionary<Quadrant, IReadOnlyList<string>>
            {
                [Quadrant.BottomLeft] = [.. ids.Take(12), .. withPickers ? Pickers : []],
                [Quadrant.BottomCenter] = [.. ids.Skip(12).Take(8)],
                [Quadrant.BottomRight] = [.. ids.Skip(20).Take(3)],
                [Quadrant.TopLeft] = [.. ids.Skip(23).Take(6)],
                [Quadrant.TopCenter] = [.. ids.Skip(29).Take(2)],
                [Quadrant.TopRight] = [.. ids.Skip(31)],
            };
            var m = ToolbarModel.Defaults with { Placed = placed, Style = new ButtonStyle(size, true, false), Buttons = custom };
            AssertMetricsInRange(m);
            foreach (var top in new[] { true, false })
                for (double room = 320; room <= 2000; room += 10)
                    AssertFit(FitRow(m, top, room), room, $"{size} pickers {withPickers} top {top} room {room}");
        }
    }

    [Fact]
    public void Custom_buttons_with_explicit_sizes_from_20_to_80_stay_within_80()
    {
        var r = new Random(80);
        foreach (var size in Enum.GetValues<ButtonSize>())
            for (var i = 0; i < 200; i++)
            {
                var style = new ButtonStyle(size, Width: r.Next(2) == 0 ? null : r.Next(20, 81), Height: r.Next(2) == 0 ? null : r.Next(20, 81));
                var custom = ToolbarPlan.Metrics(style, isCustom: true);
                Assert.InRange(custom.Width, 20, 80);
                Assert.InRange(custom.Height, 20, 80);
                Assert.True(custom.FontSize <= 22);
                if (style.Width is { } w) Assert.Equal(w, custom.Width); // a valid explicit size is kept as it is
                if (style.Height is { } h) Assert.Equal(h, custom.Height);
                var builtIn = ToolbarPlan.Metrics(style);
                Assert.True(builtIn.Width >= custom.Width && builtIn.Height >= custom.Height, $"{size} {style}");
                // A base so big that wide is 140 × 120: the custom button still stops at 80 × 80 and a glyph of 22.
                var big = ToolbarPlan.Metrics(style, true, 70, 60, 30);
                Assert.InRange(big.Width, 20, 80);
                Assert.InRange(big.Height, 20, 80);
                Assert.True(big.FontSize <= 22);
                if (size == ButtonSize.Wide && style.Width is null) Assert.Equal(140, ToolbarPlan.Metrics(style, false, 70, 60, 30).Width);
            }
    }

    [Fact]
    public void Resolve_of_24_custom_buttons_at_their_biggest_on_a_wide_bar_stays_within_limits()
    {
        var buttons = string.Join(", ", Enumerable.Range(1, 24).Select(i => $$""" "c{{i}}": { "label": "C{{i}}", "text": "t" } """));
        var overrides = string.Join(", ", Enumerable.Range(1, 24).Select(i => $$""" "c{{i}}": { "size": "wide", "width": 999, "height": 80 } """));
        var ids = string.Join(", ", Enumerable.Range(1, 24).Select(i => $"\"c{i}\""));
        var m = ToolbarModel.Resolve($$"""
            { "version": 2, "top": { "left": ["effort", "model", "provider"], "right": [] }, "items": { "left": [{{ids}}], "right": ["settings"] },
              "style": { "size": "wide", "width": 999 }, "overrides": { {{overrides}} }, "buttons": { {{buttons}} } }
            """);
        Assert.Equal(24, m.Buttons.Count);
        AssertMetricsInRange(m);
        var biggest = ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Wide, Width: 80, Height: 80), isCustom: true);
        Assert.Equal(80, biggest.Width);
        Assert.Equal(80, biggest.Height);
        Assert.All(m.Buttons, b => Assert.Equal(biggest, ToolbarPlan.Metrics(m, b.Id)));
        // The bar's width (clamped to 80) wins over the preset; the height stays wide's.
        Assert.Equal(ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Wide, Width: 80)), ToolbarPlan.Metrics(m, "settings"));
        Assert.Equal(80, ToolbarPlan.Metrics(m, "settings").Width);
        for (double room = 320; room <= 2000; room += 5) AssertFit(FitRow(m, top: false, room), room, $"room {room}");
    }

    [Fact]
    public void A_right_group_or_a_single_button_wider_than_the_room_never_throws_and_shows_no_left_button()
    {
        double[] big = [108, 108, 108, 108]; // e.g. a base grown past today's
        foreach (var hasPickers in new[] { true, false })
            foreach (var hasCenter in new[] { true, false })
                for (double room = -10; room <= 500; room += 3)
                {
                    var fit = ToolbarPlan.FitRow(room, Spacing, [36, 108], big, hasPickers, PickersMin, 108, hasCenter);
                    Assert.Equal(new RowFit(0, true, false, fit.FixedWidth), fit);
                    var single = ToolbarPlan.FitRow(room, Spacing, [108], [], hasPickers, PickersMin, 108, hasCenter);
                    Assert.True(double.IsFinite(single.FixedWidth));
                    if (room < 108) Assert.Equal(0, single.LeftShown);
                }
        // Bad numbers from a broken measure: still a fit.
        var bad = ToolbarPlan.FitRow(double.NaN, double.PositiveInfinity, [double.NaN, -1, 1e308], [double.PositiveInfinity], true, double.NaN, double.NaN, true);
        Assert.InRange(bad.LeftShown, 0, 3);
    }
}
