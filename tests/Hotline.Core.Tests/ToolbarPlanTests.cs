using Hotline.Core.Text.Localization;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>What the renderer does with a resolved toolbar.json: rows, sizes, overflow and the This PC line.</summary>
public sealed class ToolbarPlanTests
{
    private static ToolbarModel With(params (Quadrant Q, string[] Items)[] places) =>
        ToolbarModel.Defaults with
        {
            Placed = Enum.GetValues<Quadrant>().ToDictionary(q => q, q => (IReadOnlyList<string>)(places.FirstOrDefault(p => p.Q == q).Items ?? [])),
        };

    // ---- sizes ----

    // The sizes: compact half a cell, normal one, wide two; every button one cell high, wide keeps the normal glyph.
    [Theory]
    [InlineData(0.5, 18, 36, 12)] // half of a 36 x 36 cell, glyph 0.8
    [InlineData(1, 36, 36, 15)]
    [InlineData(2, 72, 36, 15)] // twice as wide, no higher, the same icon
    public void Size_presets(double size, double width, double height, double font) =>
        Assert.Equal(new ButtonMetrics(width, height, font), ToolbarPlan.Metrics(new ButtonStyle(size, false, false), 36, 36, 15));

    [Fact]
    public void A_bigger_base_scales_every_preset_and_the_height_stays_the_bases()
    {
        Assert.Equal(new ButtonMetrics(25, 44, 16), ToolbarPlan.Metrics(new ButtonStyle(0.5), 50, 44, 20));
        Assert.Equal(new ButtonMetrics(100, 44, 20), ToolbarPlan.Metrics(new ButtonStyle(2), 50, 44, 20));
    }

    [Theory]
    [InlineData(0.5, 20, 13)]
    [InlineData(1, 40, 16)]
    [InlineData(2, 80, 16)]
    [InlineData(3, 120, 16)]
    [InlineData(4, 160, 16)]
    public void The_width_sets_the_width_and_the_height_is_always_one_cell(double cells, double width, double font) =>
        Assert.Equal(new ButtonMetrics(width, ToolbarPlan.BaseHeight, font), ToolbarPlan.Metrics(new ButtonStyle(cells)));

    [Fact]
    public void A_width_that_is_not_allowed_in_memory_is_one_cell_as_is_none()
    {
        foreach (var bad in new[] { 0, 5, 1.3, -2, 42, double.NaN, double.PositiveInfinity })
            Assert.Equal(ToolbarPlan.Metrics(new ButtonStyle(1)), ToolbarPlan.Metrics(new ButtonStyle(bad)));
        Assert.Equal(ToolbarPlan.Metrics(new ButtonStyle(1)), ToolbarPlan.Metrics(new ButtonStyle()));
    }

    [Fact]
    public void Custom_and_built_in_buttons_are_sized_alike_and_one_cell_high()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 3, "bottom": { "left": ["pin", "mine"], "right": [] }, "style": { "width": 2 },
              "overrides": { "mine": { "width": 0.5 } },
              "buttons": { "mine": { "label": "Mine", "text": "hi" } } }
            """);
        Assert.Equal(ToolbarPlan.Metrics(new ButtonStyle(2)), ToolbarPlan.Metrics(m, "pin"));
        Assert.Equal(ToolbarPlan.Metrics(new ButtonStyle(0.5)), ToolbarPlan.Metrics(m, "mine"));
        Assert.All(new[] { "pin", "mine", "more" }, id => Assert.Equal(ToolbarPlan.BaseHeight, ToolbarPlan.Metrics(m, id).Height));
    }

    [Fact]
    public void The_automatic_overflow_button_is_one_cell_whatever_the_bar_size()
    {
        foreach (var size in new[] { 0.5, 2 })
        {
            var m = With((Quadrant.BottomLeft, ["pin", "captureWindow", "captureScreen", "captureRegion", "prompt", "recent", "newChat", "settings"]))
                with { Style = new ButtonStyle(size, false, false) };
            var row = ToolbarPlan.PlanRow(m, top: false, room: 3 * ToolbarPlan.Metrics(m.Style).Width, ToolbarPlan.CellGap);
            Assert.True(row.Fit.Overflow);
            Assert.Equal(ToolbarPlan.BaseWidth, row.Fit.FixedWidth - row.ButtonWidths.Values.Sum());
        }
    }

    // ---- rows ----

    [Fact]
    public void Defaults_put_the_pickers_across_the_top_and_the_buttons_in_three_groups_below()
    {
        var m = ToolbarModel.Defaults;
        Assert.Equal(new RowPlan(["effort"], ["model"], ["provider"]), ToolbarPlan.Row(m, top: true));
        Assert.Equal(new RowPlan(["pin", "captureWindow", "captureScreen", "captureRegion"], ["prompt", "continueIn"], ["recent", "newChat", "settings"]),
            ToolbarPlan.Row(m, top: false));
        Assert.Equal(["effort", "model", "provider"], ToolbarPlan.PickerOrder(m));
    }

    [Fact]
    public void Left_and_right_groups_keep_their_order()
    {
        var m = With((Quadrant.BottomLeft, ["pin", "prompt"]), (Quadrant.BottomRight, ["newChat", "settings"]), (Quadrant.TopRight, ["recent"]));
        Assert.Equal(new RowPlan(["pin", "prompt"], [], ["newChat", "settings"]), ToolbarPlan.Row(m, top: false));
        Assert.Equal(new RowPlan([], [], ["recent"]), ToolbarPlan.Row(m, top: true));
    }

    [Fact]
    public void Each_picker_is_an_entry_where_it_is_listed()
    {
        var m = With((Quadrant.BottomRight, ["newChat", "provider", "model", "settings"]));
        Assert.Equal(new RowPlan([], [], ["newChat", "provider", "model", "settings"]), ToolbarPlan.Row(m, top: false));
        Assert.Equal(["provider", "model"], ToolbarPlan.PickerOrder(m));
    }

    [Fact]
    public void Pickers_split_over_places_by_hand_are_drawn_where_each_is_listed()
    {
        var m = With((Quadrant.BottomLeft, ["pin", "model"]), (Quadrant.TopLeft, ["effort", "recent"]), (Quadrant.TopRight, ["provider", "model"]));
        Assert.Equal(new RowPlan(["pin", "model"], [], []), ToolbarPlan.Row(m, top: false));
        Assert.Equal(new RowPlan(["effort", "recent"], [], ["provider"]), ToolbarPlan.Row(m, top: true)); // a second "model" is the duplicate
        Assert.Equal(["model", "effort", "provider"], ToolbarPlan.PickerOrder(m));
    }

    [Fact]
    public void Picker_ids_are_recognised()
    {
        Assert.True(ToolbarPlan.IsPicker("model"));
        Assert.False(ToolbarPlan.IsPicker("pin"));
        Assert.False(ToolbarPlan.IsPicker("pickers"));
    }

    // ---- overflow ----

    [Theory]
    [InlineData(new[] { "pin", "effort", "prompt", "recent" }, 1, new[] { "pin", "effort" }, new[] { "prompt", "recent" })] // buttons dropped from the end, pickers kept
    [InlineData(new[] { "pin", "prompt" }, 2, new[] { "pin", "prompt" }, new string[0])]                                    // everything shown: nothing dropped
    [InlineData(new[] { "pin", "model", "prompt" }, 0, new[] { "model" }, new[] { "pin", "prompt" })]                       // nothing shown: only the pickers stay
    public void Split_drops_left_buttons_from_the_end_and_keeps_the_pickers(string[] left, int leftShown, string[] expectedShown, string[] expectedDropped)
    {
        var (shown, dropped) = ToolbarPlan.Split(left, leftShown);
        Assert.Equal(expectedShown, shown);
        Assert.Equal(expectedDropped, dropped);
    }

    // FitRow takes the whole room:the Grid's flexible column and the pickers' column (kept even when the pickers hide)
    // each cost a ColumnSpacing that Fit doesn't count. Used = everything but the pickers + the pickers' room when kept.
    private static double Used(RowFit fit, double pickersMin) => fit.FixedWidth + (fit.PickersKept ? pickersMin : 0);

    [Fact]
    public void FitRow_keeps_the_pickers_room_when_the_buttons_can_overflow_into_the_rest()
    {
        // 1 left button (36) + overflow (36) + right (36) + 5 columns' gaps (pickers, star) = 116, + pickers 150 = 266 of 302.
        var fit = ToolbarPlan.FitRow(302, 2, [36, 36, 36, 36], [36], hasPickers: true, 150, 36);
        Assert.Equal(new RowFit(1, true, true, 116), fit);
        Assert.True(Used(fit, 150) <= 302);
    }

    [Fact]
    public void FitRow_drops_the_pickers_before_letting_the_overflow_button_stick_out()
    {
        // With the pickers' room the overflow button would stick out; without it all three left buttons fit
        // (the pickers then hide in ToolbarLayout.Compute).
        var fit = ToolbarPlan.FitRow(202, 2, [36, 36, 36], [36], hasPickers: true, 150, 36);
        Assert.Equal(new RowFit(3, false, false, 154), fit);
        Assert.True(Used(fit, 150) <= 202);
    }

    [Fact]
    public void FitRow_budgets_the_pickers_column_gap_when_their_room_is_not_reserved()
    {
        // No picker available (pickersMin 0) but the pickers' column is still in the Grid: 2 buttons need 72 + 3 gaps = 78.
        Assert.Equal(new RowFit(2, false, false, 78), ToolbarPlan.FitRow(78, 2, [36, 36], [], hasPickers: true, 0, 36));
        var narrow = ToolbarPlan.FitRow(77, 2, [36, 36], [], hasPickers: true, 0, 36);
        Assert.Equal(new RowFit(0, true, false, 40), narrow);
        Assert.True(narrow.FixedWidth <= 77);
    }

    [Fact]
    public void FitRow_without_pickers_counts_only_the_flexible_column()
    {
        Assert.Equal(new RowFit(1, true, false, 76), ToolbarPlan.FitRow(102, 2, [36, 36, 36], [], hasPickers: false, 0, 36));
        Assert.Equal(new RowFit(3, false, false, 152), ToolbarPlan.FitRow(1000, 2, [36, 36, 36], [36], hasPickers: false, 0, 36));
        // Exactly the width of the whole row: everything stays.
        Assert.Equal(new RowFit(3, false, false, 152), ToolbarPlan.FitRow(152, 2, [36, 36, 36], [36], hasPickers: false, 0, 36));
    }

    [Fact]
    public void FitRow_accepts_an_overshoot_that_remains_without_the_pickers()
    {
        // Not even the one right button and the overflow button fit: the overflow button stays so the left buttons remain reachable.
        var fit = ToolbarPlan.FitRow(40, 2, [36], [36], hasPickers: true, 100, 36);
        Assert.Equal(new RowFit(0, true, false, 78), fit);
    }

    // The smallest row is one right button (36) + "..." (36) + 3 gaps around the pickers' and the flexible column = 78; the
    // whole right group (72) with "..." is 116. In between the right group gives up its first button (changed: it used to
    // stay whole, overshooting below 116). Below 78 the row really overshoots (Fit's documented case).
    [Theory]
    [InlineData(70, true)]
    [InlineData(77, true)]
    [InlineData(78, false)]
    [InlineData(90, false)]
    [InlineData(115, false)]
    [InlineData(116, false)]
    [InlineData(150, false)]
    [InlineData(199, false)]
    [InlineData(240, false)]
    [InlineData(330, false)] // pickers keep their room, 2 left buttons
    [InlineData(500, false)] // everything fits
    public void FitRow_fits_the_room_or_overshoots_only_as_documented(double room, bool overshoots)
    {
        const double spacing = 2, pickersMin = 120, overflow = 36;
        double[] left = [36, 30, 46, 36], right = [36, 36];
        // Even with no left button and the pickers hidden: the last right button + "..." + the gaps of that, "...", pickers and * columns.
        var smallest = right[^1] + overflow + spacing * 3;
        Assert.Equal(overshoots, smallest > room); // the InlineData says which case it is: both groups stay covered
        var fit = ToolbarPlan.FitRow(room, spacing, left, right, hasPickers: true, pickersMin, overflow);
        if (overshoots)
        {
            Assert.Equal(new RowFit(0, true, false, smallest, RightDropped: 1), fit);
            Assert.True(Used(fit, pickersMin) <= room + overflow + spacing, $"{fit} at {room}");
        }
        else
        {
            Assert.True(Used(fit, pickersMin) <= room, $"{fit} at {room}");
            // Re-fitted in just the room it uses (the panel shrunk to the row): the same buttons stay.
            Assert.Equal(fit, ToolbarPlan.FitRow(Used(fit, pickersMin), spacing, left, right, hasPickers: true, pickersMin, overflow));
        }
    }

    [Fact]
    public void FitRow_property_rooms_cover_kept_and_dropped_pickers()
    {
        double[] left = [36, 30, 46, 36], right = [36, 36];
        Assert.True(ToolbarPlan.FitRow(330, 2, left, right, hasPickers: true, 120, 36).PickersKept);
        // Changed: at 199 the pickers used to lose their room; now one right button goes into the menu to keep it (198 is the least).
        var tight = ToolbarPlan.FitRow(199, 2, left, right, hasPickers: true, 120, 36);
        Assert.Equal(new RowFit(0, true, true, 78, RightDropped: 1), tight);
        Assert.True(ToolbarPlan.FitRow(198, 2, left, right, hasPickers: true, 120, 36).PickersKept);
        // One room less and not even one right button, "..." and the pickers' minimum fit: the pickers give up their room.
        Assert.False(ToolbarPlan.FitRow(197, 2, left, right, hasPickers: true, 120, 36).PickersKept);
    }

    [Fact]
    public void FitRow_gives_up_right_buttons_before_the_pickers_lose_their_minimum()
    {
        double[] right = [80, 80, 80, 80, 80, 80];
        // Pickers kept at 150: the last 3 right buttons (240) + "..." (36) + 5 gaps between the 6 columns (10) + 150 = 436, the
        // least room that keeps them with 3 right buttons; one DIP less and a fourth button goes into the menu.
        var fit = ToolbarPlan.FitRow(436, 2, [], right, hasPickers: true, 150, 36);
        Assert.Equal(new RowFit(0, true, true, 286, RightDropped: 3), fit);
        Assert.Equal(4, ToolbarPlan.FitRow(435, 2, [], right, hasPickers: true, 150, 36).RightDropped);
        Assert.Equal(0, ToolbarPlan.FitRow(1000, 2, [], right, hasPickers: true, 150, 36).RightDropped); // room enough: unchanged
    }

    [Fact]
    public void Row_skips_ids_that_are_neither_built_in_nor_defined_and_reports_them()
    {
        var m = With((Quadrant.BottomLeft, ["pin", "ghost", "mine"])) with { Buttons = [new CustomButton("mine", "Mine", "E734", null, "hi", false)] };
        Assert.Equal(new RowPlan(["pin", "mine"], [], []), ToolbarPlan.Row(m, top: false));
        Assert.Equal(["ghost"], ToolbarPlan.Unknown(m));
        Assert.Empty(ToolbarPlan.Unknown(ToolbarModel.Defaults));
    }

    // ---- fill ----

    [Theory]
    [InlineData(120, 40, 40, 40)]
    [InlineData(301, 101, 100, 100)]
    [InlineData(302.9, 101, 101, 100)]
    [InlineData(900, 300, 300, 300)]
    public void Fill_spreads_buttons_across_groups_and_rounds_from_left_to_right(double room, double left, double center, double right)
    {
        var m = With((Quadrant.BottomLeft, ["pin"]), (Quadrant.BottomCenter, ["prompt"]), (Quadrant.BottomRight, ["settings"]))
            with { Overrides = new Dictionary<string, ButtonStyle>() };
        var row = ToolbarPlan.PlanRow(m, false, room, 0, fill: true);
        Assert.Equal([left, center, right], new[] { row.ButtonWidths["pin"], row.ButtonWidths["prompt"], row.ButtonWidths["settings"] });
        Assert.False(row.Fit.Overflow);
        Assert.Equal(Math.Floor(room), row.Used);
    }

    [Theory]
    [InlineData(350, 100, 200, 50)]
    [InlineData(353, 101, 202, 50)]
    [InlineData(120, 40, 60, 20)]
    public void Fill_uses_button_widths_as_shares_and_allows_wide_buttons_down_to_one_cell(double room, double pin, double prompt, double settings)
    {
        var m = With((Quadrant.BottomLeft, ["pin", "prompt"]), (Quadrant.BottomRight, ["settings"])) with
        {
            Overrides = new Dictionary<string, ButtonStyle> { ["prompt"] = new(Width: 2), ["settings"] = new(Width: 0.5) },
        };
        var row = ToolbarPlan.PlanRow(m, false, room, 0, fill: true);
        Assert.Equal([pin, prompt, settings], new[] { row.ButtonWidths["pin"], row.ButtonWidths["prompt"], row.ButtonWidths["settings"] });
        Assert.False(row.Fit.Overflow);
        Assert.Equal(room, row.Used);
    }

    [Theory]
    [InlineData(700, 200, 400, 100)]
    [InlineData(280, 80, 160, 40)]
    [InlineData(164, 40, 84, 40)]
    public void Fill_mixes_button_width_and_picker_span_shares_without_a_span_cap(double room, double pin, double model, double settings)
    {
        var m = With((Quadrant.BottomLeft, ["pin"]), (Quadrant.BottomCenter, ["model"]), (Quadrant.BottomRight, ["settings"])) with
        {
            Overrides = new Dictionary<string, ButtonStyle> { ["pin"] = new(Width: 2) },
        };
        var row = ToolbarPlan.PlanRow(m, false, room, 0, fill: true);
        Assert.Equal([pin, model, settings], new[] { row.ButtonWidths["pin"], row.PickerWidths["model"], row.ButtonWidths["settings"] });
        Assert.False(row.Fit.Overflow);
        Assert.Equal(room, row.Used);
    }

    [Theory]
    [InlineData(180, 0, 0, false, 180)]
    [InlineData(179, 2, 0, true, 179)]
    [InlineData(119, 3, 1, true, 119)]
    [InlineData(80, 3, 1, true, 80)]
    [InlineData(79, 3, 1, true, 80)]
    public void Fill_overflows_at_button_minima_in_group_order_and_shares_with_automatic_more(double room, int dropped, int rightDropped, bool overflow, double used)
    {
        var m = With((Quadrant.BottomLeft, ["pin", "captureWindow"]), (Quadrant.BottomCenter, ["prompt"]), (Quadrant.BottomRight, ["newChat", "settings"])) with
        {
            Overrides = new Dictionary<string, ButtonStyle> { ["pin"] = new(Width: 4), ["captureWindow"] = new(Width: 0.5), ["prompt"] = new(Width: 2), ["settings"] = new(Width: 4) },
        };
        var row = ToolbarPlan.PlanRow(m, false, room, 0, fill: true);
        Assert.Equal(new[] { "pin", "captureWindow", "prompt" }.Skip(3 - dropped), row.Dropped);
        Assert.Equal(new[] { "newChat" }.Take(rightDropped), row.RightDropped);
        Assert.Equal(overflow, row.Fit.Overflow);
        Assert.Equal(used, row.Used);
        Assert.Equal(row.Used, row.ButtonWidths.Values.Sum());
        Assert.All(row.ButtonWidths, p => Assert.True(p.Value >= (p.Key == "captureWindow" ? 20 : 40)));
        if (room == 179) Assert.Equal(40, row.ButtonWidths["more"]);
        if (room == 119) Assert.Equal(79, row.ButtonWidths["settings"]);
    }

    [Theory]
    [InlineData(350, 100, 200, 50)]
    [InlineData(59, 0, 40, 20)] // a half-cell more cannot shrink the last normal button below one cell
    public void Fill_keeps_a_placed_more_in_position_with_its_width_shares_and_user_menu(double room, double pin, double settings, double more)
    {
        var m = With((Quadrant.BottomLeft, ["pin"]), (Quadrant.BottomCenter, ["more"]), (Quadrant.BottomRight, ["settings"])) with
        {
            Overrides = new Dictionary<string, ButtonStyle> { ["more"] = new(Width: 0.5), ["settings"] = new(Width: 2) },
            Menu = ["recent"],
        };
        var row = ToolbarPlan.PlanRow(m, false, room, 0, fill: true);
        Assert.Equal(pin, row.ButtonWidths.GetValueOrDefault("pin"));
        Assert.Equal(settings, row.ButtonWidths["settings"]);
        Assert.Equal(more, row.ButtonWidths["more"]);
        Assert.Equal(pin > 0 ? new[] { "recent" } : new[] { "recent", "pin" }, row.Menu);
        Assert.Equal(Math.Max(60, room), row.Used);
    }

    [Theory]
    [InlineData(212, 56, 84, 72)]
    [InlineData(211, 0, 94, 117)]
    [InlineData(156, 0, 84, 72)]
    [InlineData(155, 0, 155, 0)]
    [InlineData(84, 0, 84, 0)]
    [InlineData(83, 0, 0, 0)]
    public void Fill_pickers_keep_their_current_floors_and_hide_in_the_current_order(double room, double effort, double model, double provider)
    {
        var m = With((Quadrant.BottomLeft, ["model", "effort", "provider"])) with { PickerSpans = new Dictionary<string, int>() };
        var row = ToolbarPlan.PlanRow(m, false, room, 0, fill: true);
        Assert.Equal([effort, model, provider], new[] { row.PickerWidths["effort"], row.PickerWidths["model"], row.PickerWidths["provider"] });
        Assert.Equal(effort + model + provider, row.Used);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Fill_counts_only_visible_items_and_their_gaps_and_plans_each_row_independently(double spacing)
    {
        var m = With((Quadrant.TopLeft, ["provider"]), (Quadrant.BottomLeft, ["pin", "effort"]), (Quadrant.BottomCenter, ["model"]), (Quadrant.BottomRight, ["settings"]))
            with { Overrides = new Dictionary<string, ButtonStyle>() };
        var row = ToolbarPlan.PlanRow(m, false, 601 + 2 * spacing, spacing, availablePickers: ["model", "provider"], fill: true);
        Assert.Equal(0, row.PickerWidths["effort"]);
        Assert.Equal([101.0, 400, 100], new[] { row.ButtonWidths["pin"], row.PickerWidths["model"], row.ButtonWidths["settings"] });
        Assert.Equal(601 + 2 * spacing, row.Used);
        Assert.Equal(601 + 2 * spacing, ToolbarPlan.PlanRow(m, true, 601 + 2 * spacing, spacing, fill: true).PickerWidths["provider"]);
        var hidden = ToolbarPlan.PlanRow(With((Quadrant.BottomLeft, ["model"])), false, 500, spacing, availablePickers: [], fill: true);
        Assert.Equal(0, hidden.Used);
        Assert.Empty(hidden.ButtonWidths);
    }

    [Theory]
    [InlineData(179, 45, 44)]
    [InlineData(199, 50, 49)]
    public void Fill_grows_automatic_more_as_one_share_and_rounds_in_draw_order(double room, double more, double settings)
    {
        var m = With((Quadrant.BottomLeft, ["pin", "captureWindow", "captureScreen", "captureRegion", "prompt"]), (Quadrant.BottomRight, ["settings"]))
            with { Overrides = new Dictionary<string, ButtonStyle>() };
        var row = ToolbarPlan.PlanRow(m, false, room, 0, fill: true);
        Assert.Equal(["captureScreen", "captureRegion", "prompt"], row.Dropped);
        Assert.Equal(more, row.ButtonWidths["more"]);
        Assert.Equal(settings, row.ButtonWidths["settings"]);
        Assert.Equal(room, row.Used);
    }

    [Theory]
    [InlineData(1, 40)]
    [InlineData(5, 84)]
    [InlineData(8, 84)]
    public void Fill_uses_custom_picker_spans_as_shares_and_keeps_the_existing_span_capped_floor(int span, double minimum)
    {
        var m = With((Quadrant.BottomLeft, ["pin", "model"])) with { PickerSpans = new Dictionary<string, int> { ["model"] = span } };
        var wide = ToolbarPlan.PlanRow(m, false, (span + 1) * 100, 0, fill: true);
        Assert.Equal(100, wide.ButtonWidths["pin"]);
        Assert.Equal(span * 100, wide.PickerWidths["model"]);
        Assert.Equal(minimum, ToolbarPlan.PlanRow(m, false, 40 + minimum, 0, fill: true).PickerWidths["model"]);
        Assert.Equal(0, ToolbarPlan.PlanRow(m, false, 39 + minimum, 0, fill: true).PickerWidths["model"]);
    }

    [Theory]
    [InlineData(244, 0, 0, true)]
    [InlineData(243, 2, 0, true)]
    [InlineData(204, 2, 0, true)]
    [InlineData(203, 2, 1, true)]
    [InlineData(164, 2, 1, true)]
    [InlineData(163, 0, 0, false)]
    public void Fill_reserves_picker_minima_before_overflow_and_releases_the_reserve_when_it_cannot_fit(double room, int leftDropped, int rightDropped, bool pickerKept)
    {
        var m = With((Quadrant.BottomLeft, ["pin", "prompt", "model"]), (Quadrant.BottomRight, ["newChat", "settings"]))
            with { Overrides = new Dictionary<string, ButtonStyle>() };
        var row = ToolbarPlan.PlanRow(m, false, room, 0, fill: true);
        Assert.Equal(leftDropped, row.Dropped.Count);
        Assert.Equal(rightDropped, row.RightDropped.Count);
        Assert.Equal(pickerKept, row.Fit.PickersKept);
        Assert.Equal(pickerKept, row.PickerWidths["model"] >= 84);
        Assert.Equal(room, row.Used);
    }

    [Fact]
    public void Fill_reference_rows_keep_buttons_reachable_and_use_the_room_or_only_overshoot_at_minima()
    {
        foreach (var profile in ToolbarProfiles.All)
        foreach (var size in new[] { 0.5, 1, 2, 4 })
        foreach (var top in new[] { false, true })
        foreach (var spacing in new[] { 0, 2 })
        {
            var m = ToolbarModel.Resolve(profile.Json);
            m = m with { Style = m.Style with { Width = size } };
            for (var room = 20; room <= 1200; room += 17)
            {
                var row = ToolbarPlan.PlanRow(m, top, room, spacing, fill: true);
                var expected = row.Plan.Droppable.Concat(row.Plan.Right).Where(id => !ToolbarPlan.Stays(id)).Order();
                var reachable = row.ButtonWidths.Keys.Where(id => id != "more").Concat(row.Dropped).Concat(row.RightDropped).Order();
                Assert.Equal(expected, reachable);
                Assert.All(row.ButtonWidths, p => Assert.True(p.Value >= 40 * Math.Min(1, p.Key == "more" && !row.Plan.HasMore ? 1 : ToolbarPlan.WidthCells(m.EffectiveStyle(p.Key)))));
                var specs = ToolbarPlan.PickerSpecs(m, ToolbarModel.Pickers).ToDictionary(p => p.Name);
                Assert.All(row.PickerWidths, p => Assert.True(p.Value == 0 || p.Value >= specs[p.Key].Min));
                var widths = row.ButtonWidths.Values.Concat(row.PickerWidths.Values.Where(w => w > 0)).ToList();
                Assert.All(widths, w => Assert.Equal(Math.Floor(w), w));
                Assert.Equal(widths.Sum() + spacing * Math.Max(0, widths.Count - 1), row.Used);
                if (row.Used > room)
                {
                    Assert.DoesNotContain(row.Shown, id => !ToolbarPlan.Stays(id));
                    Assert.True(row.RightShown.Count(id => !ToolbarPlan.Stays(id)) <= 1);
                    Assert.All(row.PickerWidths.Values, w => Assert.Equal(0, w));
                }
                else Assert.Equal(widths.Count == 0 ? 0 : room, row.Used);
            }
        }
    }

    [Fact]
    public void Fill_off_preserves_the_default_plan_for_every_reference_layout()
    {
        foreach (var profile in ToolbarProfiles.All)
        foreach (var room in new[] { 79, 164, 320, 680, 1600 })
        foreach (var top in new[] { false, true })
        {
            var m = ToolbarModel.Resolve(profile.Json);
            var before = ToolbarPlan.PlanRow(m, top, room, 0);
            var off = ToolbarPlan.PlanRow(m, top, room, 0, fill: false);
            Assert.Equal((before.Fit, before.Used, before.Plan), (off.Fit, off.Used, off.Plan));
            Assert.Equal(before.ButtonWidths, off.ButtonWidths);
            Assert.Equal(before.PickerWidths, off.PickerWidths);
            Assert.Equal(before.Menu, off.Menu);
        }
    }

    // ---- This PC ----

    [Fact]
    public void Report_is_ok_for_the_defaults_and_when_only_settings_is_in_the_tray_and_lists_warnings_and_errors()
    {
        Assert.Equal((true, Strings.Pc_ToolbarOk), ToolbarPlan.Report(ToolbarModel.Defaults, null));
        Assert.Equal((true, Strings.Pc_ToolbarOk + " " + Strings.Pc_ToolbarNoSettings), ToolbarPlan.Report(With((Quadrant.BottomLeft, ["pin"])), null));
        Assert.Equal((false, L.F(Strings.Pc_ToolbarWarnings, 4, "one; two; three")),
            ToolbarPlan.Report(ToolbarModel.Defaults with { Warnings = ["one", "two", "three", "four"] }, null)); // the first three
        // Settings in the "more" menu is on the bar's menu, not only in the tray.
        var inMenu = With((Quadrant.BottomLeft, ["pin", "more"])) with { Menu = ["settings"] };
        Assert.Equal((true, Strings.Pc_ToolbarOk), ToolbarPlan.Report(inMenu, null));
        Assert.Equal((false, "toolbar.json couldn't be read: x"), ToolbarPlan.Report(ToolbarModel.Defaults, "toolbar.json couldn't be read: x"));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(400, double.NaN)]
    [InlineData(double.NegativeInfinity, 0)]
    public void Fill_with_a_bad_room_or_spacing_gives_finite_widths(double room, double spacing)
    {
        var row = ToolbarPlan.PlanRow(ToolbarModel.Defaults, top: false, room, spacing, fill: true);
        Assert.True(double.IsFinite(row.Used));
        Assert.All(row.ButtonWidths.Values, w => Assert.True(double.IsFinite(w)));
    }
}
