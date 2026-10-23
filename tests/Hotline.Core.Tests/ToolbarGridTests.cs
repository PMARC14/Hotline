using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>Every item is a cell of a 40-DIP grid with its padding inside the cell (2026-10-22).</summary>
public sealed class ToolbarGridTests
{
    private static readonly string[] Four = ["pin", "captureWindow", "captureScreen", "captureRegion"];

    private static ToolbarModel Sized(ButtonSize size, params string[] ids) => ToolbarModel.Defaults with
    {
        Style = new ButtonStyle(size, false, false),
        Placed = Enum.GetValues<Quadrant>().ToDictionary(q => q, q => (IReadOnlyList<string>)(q == Quadrant.BottomLeft ? ids : [])),
    };

    [Fact]
    public void The_gap_is_zero_and_the_inset_is_one()
    {
        Assert.Equal(0, ToolbarPlan.CellGap);
        Assert.Equal(1, ToolbarPlan.CellInset);
        Assert.Equal(38, ToolbarPlan.Inner(40));
        Assert.Equal((18.0, 16.0), (ToolbarPlan.Inner(20), ToolbarPlan.Inner(18))); // a compact cell keeps a visible 18 x 16 button
        Assert.Equal(0, ToolbarPlan.Inner(1));
    }

    private static double RowWidth(ButtonSize size, int count)
    {
        var m = Sized(size, [.. Four.Take(count)]);
        return ToolbarPlan.PlanRow(m, top: false, 2000, ToolbarPlan.CellGap).Used;
    }

    [Fact]
    public void Two_normal_buttons_are_one_wide_button_and_four_compact_ones()
    {
        Assert.Equal(80, RowWidth(ButtonSize.Normal, 2));
        Assert.Equal(80, RowWidth(ButtonSize.Wide, 1));
        Assert.Equal(80, RowWidth(ButtonSize.Compact, 4));
        Assert.Equal(40, RowWidth(ButtonSize.Compact, 2));
    }

    [Theory]
    [InlineData(ButtonSize.Compact)]
    [InlineData(ButtonSize.Normal)]
    [InlineData(ButtonSize.Wide)]
    public void Row_totals_are_the_sum_of_cell_widths_and_multiples_of_the_half_cell(ButtonSize size)
    {
        foreach (var n in new[] { 1, 2, 3, 4 })
        {
            var cell = ToolbarPlan.Metrics(Sized(size).Style).Width;
            var used = RowWidth(size, n);
            Assert.Equal(n * cell, used);
            Assert.Equal(0, used % (ToolbarPlan.BaseWidth / 2));
        }
    }

    [Theory]
    [InlineData(1, 40)]
    [InlineData(3, 120)]
    [InlineData(4, 160)]
    public void A_picker_of_span_n_is_n_cells_wide_outer(int span, double outer)
    {
        var m = ToolbarEditor.SetPickerSpan(ToolbarModel.Defaults, "model", span);
        var f = ToolbarPlan.PlanRow(m, top: true, 2000, ToolbarPlan.CellGap);
        Assert.Equal(outer, f.PickerWidths["model"]);
    }

    [Fact]
    public void The_default_pickers_fill_a_440_room_exactly()
    {
        // effort 3 + model 4 + provider 4 = 11 cells of room, no gaps.
        var f = ToolbarPlan.PlanRow(ToolbarModel.Defaults, top: true, 440, ToolbarPlan.CellGap);
        Assert.Equal((120.0, 160.0, 160.0), (f.PickerWidths["effort"], f.PickerWidths["model"], f.PickerWidths["provider"]));
        Assert.Equal(440, f.Used);
    }

    [Fact]
    public void The_default_bottom_row_fits_a_440_room_with_nothing_in_the_menu()
    {
        var f = ToolbarPlan.PlanRow(ToolbarModel.Defaults, top: false, 440, ToolbarPlan.CellGap);
        Assert.Empty(f.Menu);
        Assert.Equal(9 * 40, f.Used);
    }

    [Fact]
    public void Panel_width_for_n_buttons_adds_the_padding()
    {
        Assert.Equal(10, ToolbarPlan.PanelPadding);
        Assert.Equal(460, ToolbarPlan.WidthForButtons(11));
        Assert.Equal(60, ToolbarPlan.WidthForButtons(1));
        Assert.Equal(ToolbarPlan.WidthForButtons(11), new WindowSettings().MinWidth);
    }
}
