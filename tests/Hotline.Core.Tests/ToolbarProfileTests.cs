using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>
/// Every reference layout (<see cref="ToolbarProfiles"/>) at every bar size and a sweep of panel widths, through the same
/// <see cref="ToolbarPlan.PlanRow"/> the renderer calls: nothing is lost, the pickers stay within their spans and give way in
/// order, and the row stays in the room (or overshoots only as documented).
/// </summary>
public sealed class ToolbarProfileTests
{
    private const double Spacing = ToolbarPlan.CellGap; // the rows have no column gap: each cell holds its own padding
    private const double Eps = 1e-9;
    private static readonly double[] Widths = [320, 400, 680, 1000, 1600];
    private static readonly string[] AllPickers = ["effort", "model", "provider"];
    private static readonly double[] BarWidths = [0.5, 1, 2];

    public static TheoryData<string> Names => [.. ToolbarProfiles.All.Select(p => p.Name)];

    public static TheoryData<string, double> NamesAndSizes =>
        [.. ToolbarProfiles.All.SelectMany(p => BarWidths.Select(s => (p.Name, s)))];

    private static ToolbarModel Sized(string name, double size)
    {
        var m = ToolbarModel.Resolve(ToolbarProfiles.Named(name).Json);
        return m with { Style = m.Style with { Width = size } };
    }

    private static ToolbarPlan.RowLayout Plan(ToolbarModel m, bool top, double room, IReadOnlyCollection<string>? available = null) =>
        ToolbarPlan.PlanRow(m, top, room, Spacing, available);

    private static IEnumerable<double> Sweep(double from = 320, double to = 1800, double step = 11)
    {
        for (var room = from; room <= to; room += step) yield return room;
    }

    // ---- the set itself ----

    [Fact]
    public void There_are_at_least_18_profiles_with_distinct_names_and_the_required_cases()
    {
        var names = ToolbarProfiles.All.Select(p => p.Name).ToList();
        Assert.True(names.Count >= 18, $"{names.Count} profiles");
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var required in new[]
        {
            "default", "pickers-bottom-left-before", "pickers-bottom-left-after", "pickers-bottom-centre", "pickers-bottom-right", "pickers-top-right",
            "pickers-top-centre-both-rows", "spans-all-1", "spans-all-8", "spans-mixed", "all-compact", "all-wide", "mixed-button-sizes",
            "custom-24-pickers-bottom", "only-pickers", "only-right-group", "all-items-in-one-group", "empty-bar", "damaged-file",
        })
            Assert.Contains(required, names);
        Assert.All(ToolbarProfiles.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Json)));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_profile_resolves_and_round_trips(string name)
    {
        var m = ToolbarModel.Resolve(ToolbarProfiles.Named(name).Json);
        var again = ToolbarModel.Resolve(m.ToJson());
        Assert.Equal(m, again);
        if (name == "default") Assert.Equal(ToolbarModel.Defaults, m);
        if (name != "damaged-file" && name != "all-items-in-one-group") Assert.Empty(m.Warnings);
    }

    [Fact]
    public void The_damaged_file_still_renders_and_says_what_it_fixed()
    {
        var m = ToolbarModel.Resolve(ToolbarProfiles.Named("damaged-file").Json);
        Assert.True(m.Warnings.Count >= 8, string.Join("; ", m.Warnings));
        Assert.Contains(m.Warnings, w => w.Contains("Repeated key"));
        Assert.NotEmpty(Enum.GetValues<Quadrant>().SelectMany(m.Items));
        Assert.Equal(6, m.EffectiveSpan("provider")); // the one good span survives
        Assert.Equal(3, m.EffectiveSpan("effort"));
        Assert.Equal(4, m.EffectiveSpan("model"));
        Assert.Equal("ok", Assert.Single(m.Buttons).Id);
        Assert.False(Plan(m, top: false, 680).Shown.Count == 0);
    }

    // ---- every profile at every width and size ----

    // The fixed widths for every legal left-prefix / right-suffix fit, plus each picker's hide thresholds.
    // Visit each threshold exactly and one DIP either side; the regular grid also exercises shrinking between them.
    private static IEnumerable<double> Rooms(ToolbarModel m, bool top, string[] available)
    {
        var row = ToolbarPlan.Row(m, top);
        var left = row.Droppable.Where(id => !ToolbarPlan.Stays(id)).Select(id => ToolbarPlan.Metrics(m, id).Width).ToArray();
        var right = row.Right.Where(id => !ToolbarPlan.Stays(id)).Select(id => ToolbarPlan.Metrics(m, id).Width).ToArray();
        var onBar = row.Droppable.Concat(row.Right).Where(ToolbarPlan.IsPicker).ToArray();
        var visible = ToolbarPlan.PickerSpecs(m, available).Where(p => onBar.Contains(p.Name) && p.Available).ToList();
        var reserves = new List<double> { 0 };
        foreach (var drop in ToolbarPlan.PickerHideOrder)
        {
            reserves.Add(visible.Sum(p => p.Min));
            reserves.Add(visible.Sum(p => p.Preferred));
            visible.RemoveAll(p => p.Name == drop);
        }
        var rooms = new SortedSet<double>(Widths.Concat(Sweep(from: 0, to: 2000, step: 20)));
        for (var shown = 0; shown <= left.Length; shown++)
            for (var dropped = 0; dropped <= (shown == 0 ? Math.Max(0, right.Length - 1) : 0); dropped++)
            {
                var overflow = row.HasMore || shown < left.Length || dropped > 0;
                var fixedWidth = left.Take(shown).Sum() + right.Skip(dropped).Sum()
                    + (overflow ? (row.HasMore ? ToolbarPlan.Metrics(m, ToolbarModel.MoreId).Width : ToolbarPlan.BaseWidth) : 0);
                foreach (var reserve in reserves)
                    foreach (var delta in new[] { -1, 0, 1 })
                        if (fixedWidth + reserve + delta is >= 0 and <= 2000)
                            rooms.Add(fixedWidth + reserve + delta);
            }
        return rooms;
    }

    [Theory]
    [MemberData(nameof(NamesAndSizes))]
    public void Rows_preserve_every_item_fit_the_room_and_grow_monotonically(string name, double size)
    {
        var m = Sized(name, size);
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
        Assert.Equal(m.ToJson(), ToolbarModel.Resolve(m.ToJson()).ToJson());
        var specs = ToolbarPlan.PickerSpecs(m, AllPickers).ToDictionary(s => s.Name);
        foreach (var top in new[] { true, false })
        {
            var row = ToolbarPlan.Row(m, top);
            var onBar = row.Left.Concat(row.Center).Concat(row.Right).Where(ToolbarPlan.IsPicker).ToList();
            var hasPickers = onBar.Count > 0;
            var rightIds = row.Right.Where(id => !ToolbarPlan.Stays(id)).ToList();
            var overflow = row.HasMore ? ToolbarPlan.Metrics(m, ToolbarModel.MoreId).Width : ToolbarPlan.BaseWidth;
            foreach (var available in new[] { AllPickers, ["model", "provider"], Array.Empty<string>() })
            {
                ToolbarPlan.RowLayout? previous = null;
                foreach (var room in Rooms(m, top, available))
                {
                    var where = $"{name} {size} top {top} room {room} pickers {available.Length}";
                    var f = Plan(m, top, room, available);
                    if (Widths.Contains(room))
                    {
                        var again = Plan(m, top, room, available);
                        Assert.Equal(f.Shown, again.Shown);
                        Assert.Equal(f.Dropped, again.Dropped);
                        Assert.Equal((f.Fit, f.Used), (again.Fit, again.Used));
                        Assert.Equal(f.PickerWidths.OrderBy(p => p.Key), again.PickerWidths.OrderBy(p => p.Key));
                        Assert.Equal(f.ButtonWidths.OrderBy(p => p.Key), again.ButtonWidths.OrderBy(p => p.Key));
                    }
                    if (previous is { } p)
                    {
                        // As the robustness tests define it: the pickers keep their room once they have it, and while that stays the
                        // same no button goes away; with the buttons the same, no picker goes away either. "No button goes away"
                        // counts the buttons on the bar, left and right (the right group's first ones now move into the menu too, so
                        // the left count alone is not monotone: it is 0 while the right group grows back).
                        Assert.True(f.Fit.PickersKept || !p.Fit.PickersKept, $"{where}: the pickers lost their room");
                        if (f.Fit.PickersKept == p.Fit.PickersKept)
                            Assert.True(f.ButtonWidths.Count >= p.ButtonWidths.Count, $"{where}: {p.ButtonWidths.Count} -> {f.ButtonWidths.Count}");
                        if (f.ButtonWidths.Count == p.ButtonWidths.Count && f.Fit.PickersKept == p.Fit.PickersKept)
                            Assert.True(f.PickerWidths.Values.Count(w => w > 0) >= p.PickerWidths.Values.Count(w => w > 0), $"{where}: a picker went away");
                    }
                    previous = f;
                    if (available != AllPickers) continue;
                    // Shown and dropped are the row's droppable ids, each once, each side in order.
                    Assert.Equal(row.Droppable.OrderBy(id => id), f.Shown.Concat(f.Dropped).OrderBy(id => id));
                    Assert.Equal(row.Droppable.Where(f.Shown.Contains), f.Shown);
                    Assert.Equal(row.Droppable.Where(f.Dropped.Contains), f.Dropped);
                    Assert.DoesNotContain(f.Dropped, ToolbarPlan.IsPicker);
                    // The dropped ones are the end of the non-picker buttons, as many as the fit says.
                    var buttons = row.Droppable.Where(id => !ToolbarPlan.Stays(id)).ToList(); // pickers and the more button never move into the menu
                    Assert.Equal(buttons.Skip(f.Fit.LeftShown), f.Dropped);
                    Assert.Equal(f.Dropped.Count > 0 || f.RightDropped.Count > 0 || row.HasMore, f.Fit.Overflow); // a placed more button is always drawn
                    // The right group gives up its first buttons only once no left button is left, never its last, and its ids split cleanly.
                    var rightButtons = row.Right.Where(id => !ToolbarPlan.Stays(id)).ToList();
                    Assert.Equal(rightButtons.Take(f.Fit.RightDropped), f.RightDropped);
                    Assert.True(f.RightDropped.Count == 0 || f.Fit.LeftShown == 0, where);
                    Assert.True(rightButtons.Count == 0 || f.RightDropped.Count < rightButtons.Count, where);
                    Assert.Equal(row.Right.OrderBy(id => id), f.RightShown.Concat(f.RightDropped).OrderBy(id => id));
                    Assert.Equal(row.Right.Where(f.RightShown.Contains), f.RightShown);
                    // The menu is in bar order: left, centre, then the right group.
                    Assert.Equal(f.UserMenu.Concat(f.Dropped).Concat(f.RightDropped), f.Menu);
                    Assert.Equal(row.HasMore ? ToolbarPlan.MenuItems(m) : [], f.UserMenu); // only the row with the more button lists the user's menu
                    // The row's kept buttons have a width each.
                    Assert.Equal(buttons.Take(f.Fit.LeftShown).Concat(rightButtons.Skip(f.Fit.RightDropped)).Concat(row.HasMore ? [ToolbarModel.MoreId] : []).OrderBy(id => id), f.ButtonWidths.Keys.OrderBy(id => id));
                    foreach (var (id, width) in f.ButtonWidths)
                    {
                        Assert.Equal(ToolbarPlan.Metrics(m, id).Width, width);
                        Assert.Equal(ToolbarPlan.BaseHeight, ToolbarPlan.Metrics(m, id).Height); // every button is one cell high
                        Assert.InRange(width, 0.5 * ToolbarPlan.BaseWidth, 4 * ToolbarPlan.BaseWidth);
                    }
                    Assert.True(double.IsFinite(f.Used) && f.Used >= 0, where);
                    if (onBar.Count == 0) Assert.Empty(f.PickerWidths);
                    else
                    {
                        Assert.Equal(onBar.OrderBy(p => p), f.PickerWidths.Keys.OrderBy(p => p));
                        foreach (var (id, width) in f.PickerWidths)
                        {
                            // The span times the unit is the preferred width and the maximum too; the floor and one unit the minimum.
                            Assert.True(width == 0 || (width >= specs[id].Min - Eps && width <= specs[id].Preferred + Eps), $"{where}: {id} {width}");
                            Assert.True(width == 0 || width >= Math.Min(ToolbarPlan.PickerFloor(id), specs[id].Preferred), $"{where}: {id} {width}");
                        }
                        // While buttons move into the menu to keep the pickers' room, every picker on the bar has at least its readable minimum.
                        if (f.Fit.PickersKept)
                            Assert.All(onBar, id => Assert.True(f.PickerWidths[id] >= specs[id].Min, $"{where}: {id} {f.PickerWidths[id]} under {specs[id].Min}"));
                        // Hidden only in order: effort, then provider, then model (among the pickers on the bar). Unchanged by the new
                        // minimums (56, 84, 72): the order is the hide order, whatever the minimums are.
                        var order = ToolbarPlan.PickerHideOrder.Where(onBar.Contains).ToList();
                        var hidden = order.Where(id => f.PickerWidths[id] == 0).ToList();
                        Assert.Equal(order.Take(hidden.Count), hidden);
                    }
                    if (f.Used > room + Eps)
                    {
                        // The only overshoot: not even the last right button and the "⋯" button fit (a panel under about two buttons), so
                        // nothing else shows. (Changed: a right group wider than the room used to overshoot too; now it gives up buttons.)
                        var lastRight = rightIds.Count == 0 ? 0 : ToolbarPlan.Metrics(m, rightIds[^1]).Width;
                        var smallest = lastRight + (row.HasMore || rightIds.Count > 1 || row.Droppable.Any(id => !ToolbarPlan.Stays(id)) ? overflow : 0)
                            + Spacing * (1 + (row.HasMore || rightIds.Count > 1 || row.Droppable.Any(id => !ToolbarPlan.Stays(id)) ? 1 : 0)
                                + (hasPickers ? 1 : 0) + (row.Center.Count > 0 ? 1 : 0));
                        Assert.True(smallest > room - Eps, $"{where}: overshoot {f.Used - room} with {f.Fit}, smallest {smallest}");
                        Assert.True(f.Fit.LeftShown == 0 && f.Fit.RightDropped == Math.Max(0, rightIds.Count - 1) && !f.Fit.PickersKept, $"{where}: {f.Fit}");
                        Assert.All(f.PickerWidths.Values, w => Assert.Equal(0, w));
                    }
                }
            }
        }
    }

    // ---- what the interesting layouts promise ----

    [Fact]
    public void The_two_look_profiles_style_the_pickers_and_read_without_warnings()
    {
        var outlined = ToolbarModel.Resolve(ToolbarProfiles.Named("outlined-pickers").Json);
        Assert.Empty(outlined.Warnings);
        Assert.Equal(new ButtonStyle(null, true, false, null), outlined.EffectiveStyle("provider"));
        Assert.Equal(new ButtonStyle(null, true, true, "#E81123"), outlined.EffectiveStyle("effort"));
        Assert.Equal(new ButtonStyle(null, false, false, null), outlined.EffectiveStyle("model"));

        var filled = ToolbarModel.Resolve(ToolbarProfiles.Named("filled-outlined-bar").Json);
        Assert.Empty(filled.Warnings);
        Assert.Equal(new ButtonStyle(null, true, true, "#0078D4"), filled.EffectiveStyle("effort"));
        Assert.Equal(new ButtonStyle(null, true, false, "#0078D4"), filled.EffectiveStyle("provider"));
        Assert.Equal(2, filled.EffectiveSpan("provider"));
        Assert.Equal(0.5, filled.EffectiveStyle("pin").Width);
        Assert.Equal(3, filled.EffectiveStyle("prompt").Width);
    }


    // The span times one standard button width is the width, whatever the bar size, and spare room never grows a picker past it.
    [Theory]
    [InlineData("default", 0.5, true, 1000, 120, 160, 200)]
    [InlineData("default", 1, true, 1000, 120, 160, 200)]
    [InlineData("default", 2, true, 1000, 120, 160, 200)]
    [InlineData("spans-all-1", 1, false, 1600, 40, 40, 40)]
    [InlineData("spans-all-8", 1, true, 1800, 320, 320, 320)]
    [InlineData("spans-mixed", 1, false, 1800, 40, 160, 320)]
    public void Spans_set_the_picker_widths(string name, double size, bool top, double room, double effort, double model, double provider)
    {
        var f = Plan(Sized(name, size), top, room);
        Assert.Equal((effort, model, provider), (f.PickerWidths["effort"], f.PickerWidths["model"], f.PickerWidths["provider"]));
    }

    [Theory]
    [InlineData("pickers-bottom-centre")]
    [InlineData("pickers-bottom-centre-crowded")]
    [InlineData("custom-24-wide-pickers-bottom-centre")]
    public void Pickers_in_the_centre_count_both_flexible_columns(string name)
    {
        var m = Sized(name, 1);
        Assert.All(ToolbarPlan.Row(m, top: false).Center, id => Assert.True(ToolbarPlan.IsPicker(id) || m.Buttons.Any(b => b.Id == id)));
        Assert.Contains(ToolbarPlan.Row(m, top: false).Center, ToolbarPlan.IsPicker);
        foreach (var room in Sweep(to: 2000, step: 5))
        {
            var f = Plan(m, top: false, room);
            var row = f.Plan;
            var buttonsWidth = f.ButtonWidths.Values.Sum() + (f.Fit.Overflow ? 40 : 0);
            var columns = f.ButtonWidths.Count + (f.Fit.Overflow ? 1 : 0) + 1 + 2; // the pickers' column and two flexible ones
            Assert.Equal(buttonsWidth + Spacing * (columns - 1), f.Fit.FixedWidth, 6);
            Assert.NotEmpty(row.Center);
        }
    }

    [Fact]
    public void The_empty_bar_renders_nothing_and_does_not_throw()
    {
        var m = Sized("empty-bar", 1);
        Assert.Empty(Enum.GetValues<Quadrant>().SelectMany(m.Items));
        Assert.Empty(m.Warnings);
        foreach (var top in new[] { true, false })
            foreach (var room in Widths.Append(0).Append(-5).Append(double.NaN))
            {
                var f = Plan(m, top, room);
                Assert.Empty(f.Shown);
                Assert.Empty(f.Dropped);
                Assert.Empty(f.PickerWidths);
                Assert.Empty(f.ButtonWidths);
                Assert.Equal(0, f.Used);
            }
    }

    [Fact]
    public void A_group_over_the_cap_is_cut_at_40_read_without_unknown_ids_and_repeats()
    {
        var m = Sized("all-items-in-one-group", 1);
        Assert.Equal(36, m.Items(Quadrant.BottomLeft).Count); // of 40 read, 3 unknown ids and 1 repeat were dropped; 7 past the cap never read
        Assert.Equal(36, m.Items(Quadrant.BottomLeft).Distinct().Count());
        Assert.Contains(m.Warnings, w => w.Contains("more than 40"));
        Assert.True(Plan(m, top: false, 320).Dropped.Count > 20);
    }

    [Fact]
    public void Seventeen_wide_right_buttons_at_680_keep_the_last_ones_and_put_the_first_ones_in_the_menu_in_order()
    {
        var custom = Enumerable.Range(1, 17).Select(i => new CustomButton($"c{i}", $"C{i}", "E734", null, "t", false)).ToList();
        var ids = custom.Select(c => c.Id).ToList();
        var placed = new Dictionary<Quadrant, IReadOnlyList<string>>
        {
            [Quadrant.BottomLeft] = ["pin", "prompt"], // dropped first, before any right button
            [Quadrant.BottomRight] = [.. ids, "settings", "effort", "model", "provider"],
        };
        var m = ToolbarModel.Defaults with { Placed = placed, Buttons = custom, Style = new ButtonStyle(2) };
        var f = Plan(m, top: false, 680);
        var buttons = ids.Append("settings").ToList();
        Assert.Equal(0, f.Fit.LeftShown);
        Assert.Equal(["pin", "prompt"], f.Dropped);
        Assert.True(f.Fit.RightDropped is > 0 and < 18, $"{f.Fit}");
        Assert.Equal(buttons.Take(f.Fit.RightDropped), f.RightDropped); // the first ones, in order
        Assert.Equal(buttons.Skip(f.Fit.RightDropped), f.RightShown.Where(id => !ToolbarPlan.IsPicker(id))); // the last ones stay, Settings among them
        Assert.Equal(["pin", "prompt", .. buttons.Take(f.Fit.RightDropped)], f.Menu);
        // The pickers are visible at no less than their readable minimums (and the row fits).
        Assert.True(f.Fit.PickersKept);
        Assert.True(f.PickerWidths["effort"] >= 56 && f.PickerWidths["model"] >= 84 && f.PickerWidths["provider"] >= 72, string.Join(", ", f.PickerWidths));
        Assert.True(f.Used <= 680, $"{f.Used}");
        // Not one more button than needed went: giving one back (80 + a gap) would not fit with the pickers' minimums.
        Assert.True(f.Used - f.PickerWidths.Values.Sum() + (56 + 84 + 72) + 80 + Spacing > 680 - 1e-9, $"{f.Used}");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(2)]
    public void The_24_custom_profiles_show_readable_pickers_at_680_and_320(double size)
    {
        foreach (var name in new[] { "custom-24-pickers-bottom", "custom-24-wide-pickers-bottom-centre" })
            foreach (var room in new double[] { 680, 400, 320 })
            {
                // A wide bar at 320: one 80-DIP right button, the 80-DIP "⋯" button and the pickers' 212 do not fit together.
                if (size == 2 && room == 320) continue;
                var f = Plan(Sized(name, size), top: false, room);
                var specs = ToolbarPlan.PickerSpecs(Sized(name, size), AllPickers).ToDictionary(s => s.Name);
                Assert.True(f.Used <= room + Eps, $"{name} {size} {room}: {f.Used}");
                // At least the model picker is shown (it hides last) and no picker is a chevron only.
                Assert.True(f.PickerWidths["model"] >= Math.Min(84, specs["model"].Preferred), $"{name} {size} {room}: {string.Join(", ", f.PickerWidths)}");
                Assert.All(f.PickerWidths, p => Assert.True(p.Value == 0 || p.Value >= Math.Min(ToolbarPlan.PickerFloor(p.Key), specs[p.Key].Preferred)));
            }
    }

    [Fact]
    public void A_right_group_that_fits_exactly_is_unchanged()
    {
        var m = Sized("only-right-group", 1);
        var roomy = Plan(m, top: false, 1000);
        Assert.Empty(roomy.RightDropped);
        // Shrunk to the room the whole row needs (the pickers at their minimums): no button moves.
        var least = roomy.Fit.FixedWidth + roomy.PickerWidths.Keys.Sum(id => ToolbarPlan.PickerSpecs(m, AllPickers).First(s => s.Name == id).Min) + 3 * Spacing; // as Compute counts the gaps
        var exact = Plan(m, top: false, least);
        Assert.Empty(exact.RightDropped);
        Assert.False(exact.Fit.Overflow);
    }

    [Fact]
    public void A_picker_that_is_not_available_takes_no_room_and_the_others_keep_theirs()
    {
        var m = Sized("pickers-bottom-left-after", 1);
        var f = Plan(m, top: false, 1600, ["model", "provider"]);
        Assert.Equal(0, f.PickerWidths["effort"]);
        Assert.Equal((160.0, 160.0), (f.PickerWidths["model"], f.PickerWidths["provider"]));
        var none = Plan(m, top: false, 1600, []);
        Assert.All(none.PickerWidths.Values, w => Assert.Equal(0, w));
        Assert.False(none.Fit.PickersKept);
    }
}
