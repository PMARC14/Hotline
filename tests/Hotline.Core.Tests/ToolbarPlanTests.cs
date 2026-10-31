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
    [InlineData(ButtonSize.Compact, 18, 36, 12)] // half of a 36 x 36 cell, glyph 0.8
    [InlineData(ButtonSize.Normal, 36, 36, 15)]
    [InlineData(ButtonSize.Wide, 72, 36, 15)] // twice as wide, no higher, the same icon
    public void Size_presets(ButtonSize size, double width, double height, double font) =>
        Assert.Equal(new ButtonMetrics(width, height, font), ToolbarPlan.Metrics(new ButtonStyle(size, false, false), 36, 36, 15));

    [Fact]
    public void Todays_sizes_are_20_40_and_80_by_one_cell_high()
    {
        Assert.Equal(new ButtonMetrics(20, 36, 13), ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Compact)));
        Assert.Equal(new ButtonMetrics(40, 36, 16), ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Normal)));
        Assert.Equal(new ButtonMetrics(80, 36, 16), ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Wide)));
        // A bigger base: every preset follows, the height stays the base's.
        Assert.Equal(new ButtonMetrics(25, 44, 16), ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Compact), 50, 44, 20));
        Assert.Equal(new ButtonMetrics(100, 44, 20), ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Wide), 50, 44, 20));
    }

    [Theory]
    [InlineData(0.5, 20, 13)]
    [InlineData(1, 40, 16)]
    [InlineData(2, 80, 16)]
    [InlineData(3, 120, 16)]
    [InlineData(4, 160, 16)]
    public void Cells_set_the_width_and_the_height_is_always_one_cell(double cells, double width, double font)
    {
        foreach (var size in new ButtonSize?[] { null, ButtonSize.Compact, ButtonSize.Normal, ButtonSize.Wide })
            Assert.Equal(new ButtonMetrics(width, ToolbarPlan.BaseHeight, font), ToolbarPlan.Metrics(new ButtonStyle(size, Cells: cells)));
    }

    [Fact]
    public void Cells_that_are_not_allowed_in_memory_fall_back_to_the_size_and_a_bad_size_is_normal()
    {
        foreach (var bad in new[] { 0, 5, 1.3, -2, double.NaN, double.PositiveInfinity })
            Assert.Equal(ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Wide)), ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Wide, Cells: bad)));
        Assert.Equal(ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Normal)), ToolbarPlan.Metrics(new ButtonStyle()));
        Assert.Equal(ToolbarPlan.Metrics(new ButtonStyle()), ToolbarPlan.Metrics(new ButtonStyle((ButtonSize)42)));
    }

    [Fact]
    public void Custom_and_built_in_buttons_are_sized_alike_and_one_cell_high()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 2, "items": { "left": ["pin", "mine"], "right": [] }, "style": { "size": "wide" },
              "overrides": { "mine": { "size": "compact" } },
              "buttons": { "mine": { "label": "Mine", "text": "hi" } } }
            """);
        Assert.Equal(ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Wide)), ToolbarPlan.Metrics(m, "pin"));
        Assert.Equal(ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Compact)), ToolbarPlan.Metrics(m, "mine"));
        Assert.All(new[] { "pin", "mine", "more" }, id => Assert.Equal(ToolbarPlan.BaseHeight, ToolbarPlan.Metrics(m, id).Height));
    }

    [Fact]
    public void The_automatic_overflow_button_is_one_cell_whatever_the_bar_size()
    {
        foreach (var size in new[] { ButtonSize.Compact, ButtonSize.Wide })
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
}
