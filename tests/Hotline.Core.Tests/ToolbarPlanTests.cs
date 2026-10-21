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

    [Theory]
    [InlineData(ButtonSize.Compact, 30, 28, 13)]
    [InlineData(ButtonSize.Normal, 36, 32, 15)]
    [InlineData(ButtonSize.Wide, 46, 32, 15)]
    public void Size_presets(ButtonSize size, double width, double height, double font) =>
        Assert.Equal(new ButtonMetrics(width, height, font), ToolbarPlan.Metrics(new ButtonStyle(size, false, false)));

    [Fact]
    public void Width_and_height_overrides_win_over_the_preset_and_keep_its_font() =>
        Assert.Equal(new ButtonMetrics(50, 24, 13), ToolbarPlan.Metrics(new ButtonStyle(ButtonSize.Compact, false, false, 50, 24)));

    [Fact]
    public void Missing_size_is_normal() =>
        Assert.Equal(new ButtonMetrics(36, 32, 15), ToolbarPlan.Metrics(new ButtonStyle()));

    // ---- rows ----

    [Fact]
    public void Defaults_put_the_pickers_on_top_and_the_buttons_bottom_left()
    {
        var m = ToolbarModel.Defaults;
        Assert.Equal(new RowPlan(["effort"], [], []), ToolbarPlan.Row(m, top: true));
        Assert.Equal(new RowPlan(["pin", "captureWindow", "captureScreen", "captureRegion", "prompt", "recent", "continueIn", "newChat", "settings"], [], []),
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
    public void The_pickers_are_one_entry_at_the_first_picker_listed()
    {
        var m = With((Quadrant.BottomRight, ["newChat", "provider", "model", "settings"]));
        Assert.Equal(new RowPlan([], [], ["newChat", "provider", "settings"]), ToolbarPlan.Row(m, top: false));
        Assert.Equal(["provider", "model"], ToolbarPlan.PickerOrder(m));
    }

    [Fact]
    public void Pickers_split_over_places_by_hand_still_render_once_where_the_first_one_is()
    {
        // A model built in code (the self-test) can split them; the renderer must never place the block twice.
        var m = With((Quadrant.BottomLeft, ["pin", "model"]), (Quadrant.TopLeft, ["effort", "recent"]));
        Assert.Equal(new RowPlan(["pin", "model"], [], []), ToolbarPlan.Row(m, top: false));
        Assert.Equal(new RowPlan(["recent"], [], []), ToolbarPlan.Row(m, top: true));
        Assert.Equal(["model", "effort"], ToolbarPlan.PickerOrder(m));
    }

    [Fact]
    public void Picker_ids_are_recognised()
    {
        Assert.True(ToolbarPlan.IsPicker("model"));
        Assert.False(ToolbarPlan.IsPicker("pin"));
        Assert.False(ToolbarPlan.IsPicker("pickers"));
    }

    // ---- overflow ----

    [Fact]
    public void Split_drops_left_buttons_from_the_end_and_keeps_the_pickers()
    {
        var (shown, dropped) = ToolbarPlan.Split(["pin", "effort", "prompt", "recent"], 1);
        Assert.Equal(["pin", "effort"], shown);
        Assert.Equal(["prompt", "recent"], dropped);
    }

    [Fact]
    public void Split_with_everything_shown_drops_nothing()
    {
        var (shown, dropped) = ToolbarPlan.Split(["pin", "prompt"], 2);
        Assert.Equal(["pin", "prompt"], shown);
        Assert.Empty(dropped);
    }

    [Fact]
    public void Split_with_nothing_shown_keeps_only_the_pickers()
    {
        var (shown, dropped) = ToolbarPlan.Split(["pin", "model", "prompt"], 0);
        Assert.Equal(["model"], shown);
        Assert.Equal(["pin", "prompt"], dropped);
    }

    // FitRow takes the whole room: the Grid's flexible column and the pickers' column (kept even when the pickers hide)
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
        // Even the right group alone doesn't fit: the overflow button stays so the left buttons remain reachable.
        var fit = ToolbarPlan.FitRow(40, 2, [36], [36], hasPickers: true, 100, 36);
        Assert.Equal(new RowFit(0, true, false, 78), fit);
    }

    // Right group 72 + its gaps fit from 78 DIP; with the overflow button the smallest row is 116 (right 72 + "..." 36 +
    // 4 gaps around the pickers' and the flexible column). Below 116 the row really overshoots (Fit's documented case).
    [Theory]
    [InlineData(80, true)]
    [InlineData(90, true)]
    [InlineData(115, true)]
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
        // Even with no left button and the pickers hidden: right group + "..." + the gaps of right, "...", pickers and * columns.
        var smallest = right.Sum() + overflow + spacing * (right.Length + 2);
        Assert.Equal(overshoots, smallest > room); // the InlineData says which case it is: both groups stay covered
        var fit = ToolbarPlan.FitRow(room, spacing, left, right, hasPickers: true, pickersMin, overflow);
        if (overshoots)
        {
            Assert.Equal(new RowFit(0, true, false, smallest), fit);
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
        Assert.False(ToolbarPlan.FitRow(199, 2, left, right, hasPickers: true, 120, 36).PickersKept);
    }

    [Fact]
    public void Row_skips_ids_that_are_neither_built_in_nor_defined_and_reports_them()
    {
        var m = With((Quadrant.BottomLeft, ["pin", "ghost", "mine"])) with { Buttons = [new CustomButton("mine", "Mine", "E734", null, "hi", false)] };
        Assert.Equal(new RowPlan(["pin", "mine"], [], []), ToolbarPlan.Row(m, top: false));
        Assert.Equal(["ghost"], ToolbarPlan.Unknown(m));
        Assert.Empty(ToolbarPlan.Unknown(ToolbarModel.Defaults));
    }

    [Fact]
    public void Row_places_an_id_listed_twice_only_where_it_is_first()
    {
        var m = With((Quadrant.BottomLeft, ["pin"]), (Quadrant.TopLeft, ["pin", "recent"]));
        Assert.Equal(new RowPlan(["pin"], [], []), ToolbarPlan.Row(m, top: false));
        Assert.Equal(new RowPlan(["recent"], [], []), ToolbarPlan.Row(m, top: true));
    }

    // ---- This PC ----

    [Fact]
    public void Report_is_fine_for_the_defaults()
    {
        var (ok, detail) = ToolbarPlan.Report(ToolbarModel.Defaults, null);
        Assert.True(ok);
        Assert.Equal(Strings.Pc_ToolbarOk, detail);
    }

    [Fact]
    public void Report_lists_the_first_warnings()
    {
        var m = ToolbarModel.Defaults with { Warnings = ["one", "two", "three", "four"] };
        var (ok, detail) = ToolbarPlan.Report(m, null);
        Assert.False(ok);
        Assert.Equal(L.F(Strings.Pc_ToolbarWarnings, 4, "one; two; three"), detail);
    }

    [Fact]
    public void Report_says_when_settings_is_only_in_the_tray()
    {
        var m = With((Quadrant.BottomLeft, ["pin"]));
        var (ok, detail) = ToolbarPlan.Report(m, null);
        Assert.True(ok);
        Assert.Equal(Strings.Pc_ToolbarOk + " " + Strings.Pc_ToolbarNoSettings, detail);
    }

    [Fact]
    public void Report_shows_a_read_error()
    {
        var (ok, detail) = ToolbarPlan.Report(ToolbarModel.Defaults, "toolbar.json couldn't be read: x");
        Assert.False(ok);
        Assert.Equal("toolbar.json couldn't be read: x", detail);
    }
}
