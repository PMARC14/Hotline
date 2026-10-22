using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>The centre group of each row: reading, writing, the plan, the overflow order and the editor's arrows.</summary>
public class ToolbarCentreTests
{
    private const Quadrant BL = Quadrant.BottomLeft, BC = Quadrant.BottomCenter, BR = Quadrant.BottomRight;
    private const Quadrant TL = Quadrant.TopLeft, TC = Quadrant.TopCenter, TR = Quadrant.TopRight;

    private static ToolbarModel Bar(string items, string top) => ToolbarModel.Resolve($$"""{ "version": 2, "items": {{items}}, "top": {{top}} }""");

    [Fact]
    public void The_centre_group_is_read_in_both_rows_and_written_back()
    {
        var m = Bar("""{ "left": ["pin"], "center": ["prompt", "recent"], "right": ["settings"] }""",
                    """{ "left": ["effort", "model", "provider"], "center": ["newChat"], "right": [] }""");
        Assert.Equal(["prompt", "recent"], m.Items(BC));
        Assert.Equal(["newChat"], m.Items(TC));
        Assert.Empty(m.Warnings);
        var again = ToolbarModel.Resolve(m.ToJson());
        Assert.Equal(m, again);
        Assert.Contains("\"center\": [\"prompt\", \"recent\"]", m.ToJson());
    }

    [Fact]
    public void A_file_without_centre_groups_still_reads_and_writes_an_empty_centre()
    {
        var m = Bar("""{ "left": ["pin"], "right": ["settings"] }""", """{ "left": ["effort"], "right": [] }""");
        Assert.Empty(m.Items(BC));
        Assert.Empty(m.Items(TC));
        Assert.Contains("\"center\": []", m.ToJson());
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
    }

    [Fact]
    public void An_explicit_empty_bar_needs_no_centre_group()
    {
        var m = Bar("""{ "left": [], "right": [] }""", """{ "left": [], "right": [] }""");
        Assert.All(Enum.GetValues<Quadrant>(), q => Assert.Empty(m.Items(q)));
        Assert.DoesNotContain(m.Warnings, w => w.Contains("no items"));
        // but a missing left or right group is still "not explicit": the defaults apply
        var broken = Bar("""{ "left": [], "center": [] }""", """{ "left": [], "right": [] }""");
        Assert.Equal(ToolbarModel.Defaults.Items(BL), broken.Items(BL));
    }

    [Fact]
    public void A_centre_group_that_is_not_a_list_is_ignored_with_a_warning()
    {
        var m = Bar("""{ "left": ["pin"], "center": 5, "right": [] }""", """{ "left": ["effort"], "right": [] }""");
        Assert.Empty(m.Items(BC));
        Assert.Equal(["pin"], m.Items(BL));
        Assert.Contains(m.Warnings, w => w.Contains("items.center"));
    }

    [Fact]
    public void An_item_listed_in_two_areas_keeps_the_first_place_in_area_order()
    {
        // order: bottom left, bottom centre, bottom right, top left, top centre, top right
        var m = Bar("""{ "left": [], "center": ["pin"], "right": ["pin"] }""", """{ "left": ["pin"], "center": ["pin"], "right": ["pin"] }""");
        Assert.Equal(["pin"], m.Items(BC));
        Assert.Empty(m.Items(BR));
        Assert.Empty(m.Items(TL));
        Assert.Empty(m.Items(TC));
        Assert.Empty(m.Items(TR));
    }

    [Fact]
    public void Pickers_stay_in_the_centre_or_the_right_group_where_they_are_listed()
    {
        var m = Bar("""{ "left": ["pin"], "center": ["effort", "prompt"], "right": ["model"] }""", """{ "left": [], "right": ["provider"] }""");
        Assert.Equal(["effort", "prompt"], m.Items(BC));
        Assert.Equal(["model"], m.Items(BR));
        Assert.Equal(["provider"], m.Items(TR));
    }

    [Fact]
    public void A_row_plan_holds_left_centre_and_right_and_what_can_drop_is_left_then_centre()
    {
        var m = Bar("""{ "left": ["pin", "prompt"], "center": ["recent", "newChat"], "right": ["settings"] }""", """{ "left": [], "right": [] }""");
        var plan = ToolbarPlan.Row(m, top: false);
        Assert.Equal(["pin", "prompt"], plan.Left);
        Assert.Equal(["recent", "newChat"], plan.Center);
        Assert.Equal(["settings"], plan.Right);
        Assert.Equal(["pin", "prompt", "recent", "newChat"], plan.Droppable);
        Assert.Equal(new RowPlan(["pin", "prompt"], ["recent", "newChat"], ["settings"]), plan);
        Assert.NotEqual(plan, new RowPlan(["pin", "prompt"], [], ["settings", "recent", "newChat"]));
    }

    [Theory]
    [InlineData(4, new[] { "pin", "prompt", "recent", "newChat" }, new string[0])]
    [InlineData(3, new[] { "pin", "prompt", "recent" }, new[] { "newChat" })]   // the centre's last button goes first
    [InlineData(2, new[] { "pin", "prompt" }, new[] { "recent", "newChat" })]
    [InlineData(1, new[] { "pin" }, new[] { "prompt", "recent", "newChat" })]   // only then the left group loses its end
    [InlineData(0, new string[0], new[] { "pin", "prompt", "recent", "newChat" })]
    public void The_overflow_takes_the_centre_before_the_left_group_and_keeps_order(int shown, string[] kept, string[] dropped)
    {
        var plan = ToolbarPlan.Row(Bar("""{ "left": ["pin", "prompt"], "center": ["recent", "newChat"], "right": ["settings"] }""",
                                       """{ "left": [], "right": [] }"""), top: false);
        var (keptIds, droppedIds) = ToolbarPlan.Split(plan.Droppable, shown);
        Assert.Equal(kept, keptIds);
        Assert.Equal(dropped, droppedIds);
    }

    [Fact]
    public void A_centre_group_costs_a_second_flexible_column_in_the_fit()
    {
        double[] droppable = [36, 36, 36], right = [36];
        var without = ToolbarPlan.FitRow(500, 4, droppable, right, hasPickers: false, pickersMin: 0, overflowWidth: 32);
        var with = ToolbarPlan.FitRow(500, 4, droppable, right, hasPickers: false, pickersMin: 0, overflowWidth: 32, hasCenter: true);
        Assert.Equal(3, without.LeftShown);
        Assert.Equal(3, with.LeftShown);
        Assert.Equal(160, without.FixedWidth); // 4 buttons + 1 flexible column, 4 gaps
        Assert.Equal(164, with.FixedWidth);    // + the second flexible column's gap
    }

    [Fact]
    public void The_second_flexible_column_can_push_a_button_into_the_menu_on_a_tight_row()
    {
        double[] droppable = [36, 36, 36], right = [36];
        var tight = ToolbarPlan.FitRow(164, 4, droppable, right, hasPickers: false, pickersMin: 0, overflowWidth: 32);
        var tightWithCentre = ToolbarPlan.FitRow(164, 4, droppable, right, hasPickers: false, pickersMin: 0, overflowWidth: 32, hasCenter: true);
        Assert.Equal(3, tight.LeftShown);
        Assert.Equal(3, tightWithCentre.LeftShown); // 164 is exactly what four buttons and two flexible columns need
        var tighter = ToolbarPlan.FitRow(163, 4, droppable, right, hasPickers: false, pickersMin: 0, overflowWidth: 32, hasCenter: true);
        Assert.True(tighter.LeftShown < 3 && tighter.Overflow);
    }

    [Theory]
    [InlineData(BL, false, true)]
    [InlineData(BC, true, true)]
    [InlineData(BR, true, false)]
    [InlineData(TL, false, true)]
    [InlineData(TC, true, true)]
    [InlineData(TR, true, false)]
    public void The_arrows_offer_left_and_right_by_column(Quadrant q, bool canLeft, bool canRight)
    {
        var m = Bar("""{ "left": ["pin"], "center": ["prompt"], "right": ["settings"] }""", """{ "left": ["effort"], "center": ["recent"], "right": ["newChat"] }""");
        var unit = ToolbarEditorView.Units(m, q).Single();
        Assert.Equal(canLeft, unit.CanShiftLeft);
        Assert.Equal(canRight, unit.CanShiftRight);
        Assert.Equal(q is BL or BC or BR, unit.CanShiftUp);
        Assert.Equal(q is TL or TC or TR, unit.CanShiftDown);
    }

    [Fact]
    public void Dragging_between_the_three_areas_of_a_row_works_both_ways()
    {
        var m = Bar("""{ "left": ["pin", "prompt"], "center": [], "right": ["settings"] }""", """{ "left": [], "right": [] }""");
        var toCentre = ToolbarEditor.Drop(m, "prompt", BC, 0);
        Assert.Equal(["pin"], toCentre.Items(BL));
        Assert.Equal(["prompt"], toCentre.Items(BC));
        var toRight = ToolbarEditor.Drop(toCentre, "prompt", BR, 0);
        Assert.Equal(["prompt", "settings"], toRight.Items(BR));
        Assert.Empty(toRight.Items(BC));
        Assert.Equal(toRight, ToolbarModel.Resolve(toRight.ToJson()));
    }
}
