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

    public static TheoryData<string> Names => [.. ToolbarProfiles.All.Select(p => p.Name)];

    public static TheoryData<string, ButtonSize> NamesAndSizes =>
        [.. ToolbarProfiles.All.SelectMany(p => Enum.GetValues<ButtonSize>().Select(s => (p.Name, s)))];

    private static ToolbarModel Sized(string name, ButtonSize size)
    {
        var m = ToolbarModel.Resolve(ToolbarProfiles.Named(name).Json);
        return m with { Style = m.Style with { Size = size } };
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

    [Fact]
    public void The_default_profile_is_the_default_bar()
    {
        var m = ToolbarModel.Resolve(ToolbarProfiles.Named("default").Json);
        Assert.Equal(ToolbarModel.Defaults, m);
        Assert.Empty(m.Warnings);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_profile_resolves_and_round_trips(string name)
    {
        var m = ToolbarModel.Resolve(ToolbarProfiles.Named(name).Json);
        var again = ToolbarModel.Resolve(m.ToJson());
        Assert.Equal(m, again);
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
        Assert.Equal(2, m.EffectiveSpan("effort"));
        Assert.Equal(4, m.EffectiveSpan("model"));
        Assert.Equal("ok", Assert.Single(m.Buttons).Id);
        Assert.False(Plan(m, top: false, 680).Shown.Count == 0);
    }

    // ---- every profile at every width and size ----

    [Theory]
    [MemberData(nameof(NamesAndSizes))]
    public void Every_row_is_planned_without_losing_anything(string name, ButtonSize size)
    {
        var m = Sized(name, size);
        foreach (var top in new[] { true, false })
        {
            var row = ToolbarPlan.Row(m, top);
            foreach (var room in Widths.Concat(Sweep()))
            {
                var where = $"{name} {size} top {top} room {room}";
                var f = Plan(m, top, room);
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
                // The row's kept buttons have a width each.
                Assert.Equal(buttons.Take(f.Fit.LeftShown).Concat(rightButtons.Skip(f.Fit.RightDropped)).Concat(row.HasMore ? [ToolbarModel.MoreId] : []).OrderBy(id => id), f.ButtonWidths.Keys.OrderBy(id => id));
                foreach (var (id, width) in f.ButtonWidths)
                {
                    Assert.Equal(ToolbarPlan.Metrics(m, id).Width, width);
                    if (m.Buttons.Any(b => b.Id == id)) Assert.InRange(ToolbarPlan.Metrics(m, id).Height, 20, 80);
                    if (m.Buttons.Any(b => b.Id == id)) Assert.InRange(width, 20, 80);
                }
                Assert.True(double.IsFinite(f.Used) && f.Used >= 0, where);
            }
        }
    }

    [Theory]
    [MemberData(nameof(NamesAndSizes))]
    public void Picker_widths_stay_within_their_spans_and_hide_in_order(string name, ButtonSize size)
    {
        var m = Sized(name, size);
        var specs = ToolbarPlan.PickerSpecs(m, AllPickers).ToDictionary(s => s.Name);
        foreach (var top in new[] { true, false })
        {
            // Each row plans its own pickers (they can sit in either row, in any group).
            var row = ToolbarPlan.Row(m, top);
            var onBar = row.Left.Concat(row.Center).Concat(row.Right).Where(ToolbarPlan.IsPicker).ToList();
            foreach (var room in Widths.Concat(Sweep()))
            {
                var where = $"{name} {size} top {top} room {room}";
                var f = Plan(m, top, room);
                if (onBar.Count == 0) { Assert.Empty(f.PickerWidths); continue; }
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
        }
    }

    [Theory]
    [MemberData(nameof(NamesAndSizes))]
    public void The_row_stays_in_the_room_except_for_the_documented_overshoot(string name, ButtonSize size)
    {
        var m = Sized(name, size);
        foreach (var top in new[] { true, false })
        {
            var row = ToolbarPlan.Row(m, top);
            var hasPickers = row.Droppable.Concat(row.Right).Any(ToolbarPlan.IsPicker);
            var rightIds = row.Right.Where(id => !ToolbarPlan.Stays(id)).ToList();
            var overflow = row.HasMore ? ToolbarPlan.Metrics(m, ToolbarModel.MoreId).Width : ToolbarPlan.Metrics(m.Style).Width;
            foreach (var room in Widths.Concat(Sweep(to: 2000, step: 7)))
            {
                var where = $"{name} {size} top {top} room {room}";
                var f = Plan(m, top, room);
                if (f.Used <= room + Eps) continue;
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

    [Theory]
    [MemberData(nameof(NamesAndSizes))]
    public void Planning_is_repeatable_and_more_room_never_hides_what_was_shown(string name, ButtonSize size)
    {
        var m = Sized(name, size);
        foreach (var top in new[] { true, false })
            foreach (var available in new[] { AllPickers, ["model", "provider"], Array.Empty<string>() })
            {
                ToolbarPlan.RowLayout? previous = null;
                foreach (var room in Sweep(to: 2000, step: 3))
                {
                    var where = $"{name} {size} top {top} room {room} pickers {available.Length}";
                    var f = Plan(m, top, room, available);
                    var again = Plan(m, top, room, available);
                    Assert.Equal(f.Shown, again.Shown);
                    Assert.Equal(f.Dropped, again.Dropped);
                    Assert.Equal((f.Fit, f.Used), (again.Fit, again.Used));
                    Assert.Equal(f.PickerWidths.OrderBy(p => p.Key), again.PickerWidths.OrderBy(p => p.Key));
                    Assert.Equal(f.ButtonWidths.OrderBy(p => p.Key), again.ButtonWidths.OrderBy(p => p.Key));
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
                }
            }
    }

    [Theory]
    [MemberData(nameof(NamesAndSizes))]
    public void Resolve_of_the_sized_bar_is_stable(string name, ButtonSize size)
    {
        var m = Sized(name, size);
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
        Assert.Equal(m.ToJson(), ToolbarModel.Resolve(m.ToJson()).ToJson());
    }

    // ---- what the interesting layouts promise ----

    [Fact]
    public void The_default_pickers_are_120_160_and_160_wide_at_every_bar_size()
    {
        // Changed: they used to scale with the bar size (compact 40/80/60, wide 160/320/240); the unit is now one standard button width.
        foreach (var size in Enum.GetValues<ButtonSize>())
        {
            var f = Plan(Sized("default", size), top: true, 1000);
            Assert.Equal((120.0, 160.0, 160.0), (f.PickerWidths["effort"], f.PickerWidths["model"], f.PickerWidths["provider"]));
        }
    }

    [Fact]
    public void Spans_set_the_widths_and_spare_room_never_grows_past_them()
    {
        var one = Plan(Sized("spans-all-1", ButtonSize.Normal), top: false, 1600);
        Assert.All(one.PickerWidths.Values, w => Assert.Equal(40, w));
        var eight = Plan(Sized("spans-all-8", ButtonSize.Normal), top: true, 1800);
        Assert.All(eight.PickerWidths.Values, w => Assert.Equal(320, w));
        var mixed = Plan(Sized("spans-mixed", ButtonSize.Normal), top: false, 1800);
        Assert.Equal((40.0, 160.0, 320.0), (mixed.PickerWidths["effort"], mixed.PickerWidths["model"], mixed.PickerWidths["provider"]));
    }

    [Fact]
    public void Pickers_before_the_buttons_keep_their_place()
    {
        var m = Sized("pickers-bottom-left-before", ButtonSize.Normal);
        Assert.Equal("effort", ToolbarPlan.Row(m, top: false).Left[0]);
        foreach (var room in Sweep())
        {
            var f = Plan(m, top: false, room);
            Assert.Equal("effort", f.Shown[0]); // buttons go into the menu from the end; the pickers stay in front
            Assert.Equal(f.Plan.Left.Where(f.Shown.Contains), f.Shown);
        }
        // Narrow: some buttons are in the menu, the pickers still have their minimum.
        var narrow = Plan(m, top: false, 320);
        Assert.True(narrow.Fit.Overflow && narrow.Fit.PickersKept);
        Assert.All(narrow.PickerWidths.Values, w => Assert.True(w >= 40));
    }

    [Fact]
    public void Pickers_after_the_buttons_come_last_among_the_shown()
    {
        var m = Sized("pickers-bottom-left-after", ButtonSize.Normal);
        foreach (var room in Sweep())
        {
            var f = Plan(m, top: false, room);
            Assert.True(ToolbarPlan.IsPicker(f.Shown[^1]), $"room {room}");
            Assert.DoesNotContain(f.Dropped, ToolbarPlan.IsPicker);
        }
        var wide = Plan(m, top: false, 1600);
        Assert.Empty(wide.Dropped);
    }

    [Theory]
    [InlineData("pickers-bottom-centre")]
    [InlineData("pickers-bottom-centre-crowded")]
    [InlineData("custom-24-wide-pickers-bottom-centre")]
    public void Pickers_in_the_centre_count_both_flexible_columns(string name)
    {
        var m = Sized(name, ButtonSize.Normal);
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
    public void Pickers_on_the_right_never_move_into_the_menu()
    {
        var m = Sized("pickers-bottom-right", ButtonSize.Normal);
        foreach (var room in Sweep())
        {
            var f = Plan(m, top: false, room);
            Assert.Contains(ToolbarPlan.Row(m, top: false).Right.First(ToolbarPlan.IsPicker), ToolbarPlan.Row(m, top: false).Right);
            Assert.DoesNotContain(f.Dropped, ToolbarPlan.IsPicker);
            Assert.DoesNotContain(f.RightDropped, ToolbarPlan.IsPicker);
            Assert.Contains(f.RightShown, ToolbarPlan.IsPicker);
        }
    }

    [Theory]
    [InlineData(ButtonSize.Compact)]
    [InlineData(ButtonSize.Normal)]
    [InlineData(ButtonSize.Wide)]
    public void A_row_of_only_pickers_has_no_overflow_button(ButtonSize size)
    {
        var m = Sized("only-pickers", size);
        foreach (var room in Sweep(from: 100))
        {
            var f = Plan(m, top: false, room);
            Assert.False(f.Fit.Overflow);
            Assert.Empty(f.Dropped);
            Assert.Empty(f.ButtonWidths);
            Assert.True(f.Used <= room + Eps || room < ToolbarPlan.PickerFloor("effort") + 3 * Spacing, $"room {room}: {f.Used}");
        }
        var top = Plan(m, top: true, 680);
        Assert.Empty(top.PickerWidths);
        Assert.Equal(0, top.Used);
    }

    [Fact]
    public void The_empty_bar_renders_nothing_and_does_not_throw()
    {
        var m = Sized("empty-bar", ButtonSize.Normal);
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
    public void Only_a_right_group_drops_nothing_while_it_fits_and_then_its_first_buttons_from_the_left()
    {
        var m = Sized("only-right-group", ButtonSize.Normal);
        var right = ToolbarPlan.Row(m, top: false).Right; // seven buttons and the pickers
        var buttons = right.Where(id => !ToolbarPlan.IsPicker(id)).ToList();
        var dropped = 0;
        foreach (var room in Sweep(from: 50))
        {
            var f = Plan(m, top: false, room);
            Assert.Empty(f.Dropped); // there is no left or centre button
            Assert.Equal(f.RightDropped.Count > 0, f.Fit.Overflow);
            // Changed: it used to stay whole at any width; now its first buttons go and its last ones (Settings) stay.
            Assert.Equal(buttons.Take(f.RightDropped.Count), f.RightDropped);
            Assert.Equal("settings", f.RightShown.Last(id => !ToolbarPlan.IsPicker(id)));
            if (f.RightDropped.Count > 0) dropped++;
        }
        Assert.True(dropped > 0);
        Assert.Empty(Plan(m, top: false, 1800).RightDropped); // plenty of room: whole
    }

    [Fact]
    public void All_the_items_in_one_group_keep_their_order_in_the_menu()
    {
        var m = Sized("all-items-in-one-group", ButtonSize.Normal);
        Assert.Equal(36, m.Items(Quadrant.BottomLeft).Count); // of 40 read, 3 unknown ids and 1 repeat were dropped; 7 past the cap never read
        Assert.Equal(36, m.Items(Quadrant.BottomLeft).Distinct().Count());
        Assert.Contains(m.Warnings, w => w.Contains("more than 40"));
        var narrow = Plan(m, top: false, 320);
        var all = ToolbarPlan.Row(m, top: false).Droppable.Where(id => !ToolbarPlan.IsPicker(id)).ToList();
        Assert.Equal(all.TakeLast(narrow.Dropped.Count), narrow.Dropped);
        Assert.True(narrow.Dropped.Count > 20);
        Assert.Equal(all.Take(narrow.Fit.LeftShown), narrow.Shown.Where(id => !ToolbarPlan.IsPicker(id)));
    }

    [Fact]
    public void Twenty_four_custom_buttons_around_the_pickers_fit_from_320_at_every_size()
    {
        foreach (var name in new[] { "custom-24-pickers-bottom", "custom-24-wide-pickers-bottom-centre" })
            foreach (var size in Enum.GetValues<ButtonSize>())
            {
                var m = Sized(name, size);
                Assert.Equal(24, m.Buttons.Count);
                foreach (var room in Sweep())
                {
                    var f = Plan(m, top: false, room);
                    Assert.Equal(f.Fit.LeftShown < ToolbarPlan.Row(m, top: false).Droppable.Count(id => !ToolbarPlan.IsPicker(id)) || f.Fit.RightDropped > 0, f.Fit.Overflow);
                    Assert.True(f.Used <= room + Eps, $"{name} {size} {room}: {f.Used}"); // changed: no overshoot from 320 up
                }
            }
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
        var m = ToolbarModel.Defaults with { Placed = placed, Buttons = custom, Style = new ButtonStyle(ButtonSize.Wide) };
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
    [InlineData(ButtonSize.Compact)]
    [InlineData(ButtonSize.Normal)]
    [InlineData(ButtonSize.Wide)]
    public void The_24_custom_profiles_show_readable_pickers_at_680_and_320(ButtonSize size)
    {
        foreach (var name in new[] { "custom-24-pickers-bottom", "custom-24-wide-pickers-bottom-centre" })
            foreach (var room in new double[] { 680, 400, 320 })
            {
                // A wide bar at 320: one 80-DIP right button, the 80-DIP "⋯" button and the pickers' 212 do not fit together.
                if (size == ButtonSize.Wide && room == 320) continue;
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
        var m = Sized("only-right-group", ButtonSize.Normal);
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
        var m = Sized("pickers-bottom-left-after", ButtonSize.Normal);
        var f = Plan(m, top: false, 1600, ["model", "provider"]);
        Assert.Equal(0, f.PickerWidths["effort"]);
        Assert.Equal((160.0, 120.0), (f.PickerWidths["model"], f.PickerWidths["provider"]));
        var none = Plan(m, top: false, 1600, []);
        Assert.All(none.PickerWidths.Values, w => Assert.Equal(0, w));
        Assert.False(none.Fit.PickersKept);
    }
}
