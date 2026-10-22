using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>The optional "more" button and toolbar.json's "menu": reading, planning (merging with the automatic overflow) and editing.</summary>
public sealed class ToolbarMoreMenuTests
{
    private const double Spacing = 2;
    private const double Eps = 1e-9;

    private static ToolbarModel Read(string bottomLeft, string bottomRight, string menu, string extra = "") =>
        ToolbarModel.Resolve($$"""
            { "version": 2, "items": { "left": [{{bottomLeft}}], "right": [{{bottomRight}}] }, "menu": [{{menu}}] {{extra}} }
            """);

    private static void AssertValid(ToolbarModel m) => Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));

    private static void AssertEdit(ToolbarModel before, ToolbarModel after)
    {
        Assert.NotSame(before, after);
        Assert.Equal(after, ToolbarModel.Resolve(after.ToJson()));
        Assert.Empty(ToolbarModel.Resolve(after.ToJson()).Warnings);
    }

    // ---- reading ----

    [Fact]
    public void More_is_a_built_in_and_the_menu_reads_in_order_and_round_trips()
    {
        Assert.Contains("more", ToolbarModel.BuiltIn);
        var m = Read("\"pin\", \"captureWindow\"", "\"settings\", \"more\"", "\"recent\", \"captureScreen\"");
        Assert.Empty(m.Warnings);
        Assert.Equal(["recent", "captureScreen"], m.Menu);
        AssertValid(m);
        Assert.Empty(ToolbarModel.Resolve(m.ToJson()).Warnings);
        Assert.Contains("\"menu\"", m.ToJson());
    }

    [Fact]
    public void Menu_is_part_of_equality()
    {
        var a = Read("\"pin\"", "\"more\"", "\"recent\"");
        var b = Read("\"pin\"", "\"more\"", "\"newChat\"");
        Assert.NotEqual(a, b);
        Assert.Equal(a, Read("\"pin\"", "\"more\"", "\"recent\""));
    }

    [Fact]
    public void Menu_drops_unknown_pickers_more_repeats_and_items_on_the_bar_each_with_a_warning()
    {
        var m = Read("\"pin\", \"prompt\"", "\"more\"", "\"nobody\", \"effort\", \"more\", \"recent\", \"RECENT\", \"prompt\", 5, \"newChat\"");
        Assert.Equal(["recent", "newChat"], m.Menu);
        Assert.Equal(["pin", "prompt"], m.Items(Quadrant.BottomLeft)); // an item on the bar stays there
        Assert.Contains(m.Warnings, w => w.Contains("nobody"));
        Assert.Contains(m.Warnings, w => w.Contains("effort"));
        Assert.Contains(m.Warnings, w => w.Contains("prompt"));
        Assert.True(m.Warnings.Count >= 6, string.Join("; ", m.Warnings));
        AssertValid(m);
    }

    [Fact]
    public void Menu_entries_without_a_more_button_are_ignored_with_one_warning()
    {
        var m = Read("\"pin\"", "\"settings\"", "\"recent\", \"newChat\"");
        Assert.Empty(m.Menu);
        Assert.Equal("menu items need a more button; they are ignored", Assert.Single(m.Warnings));
    }

    [Fact]
    public void A_menu_that_is_not_a_list_is_ignored_and_custom_buttons_can_be_in_it()
    {
        var bad = ToolbarModel.Resolve("""{ "version": 2, "items": { "left": ["pin", "more"] }, "menu": "recent" }""");
        Assert.Empty(bad.Menu);
        Assert.Contains(bad.Warnings, w => w.Contains("menu"));
        var custom = ToolbarModel.Resolve("""
            { "version": 2, "items": { "left": ["pin", "more"] }, "menu": ["c1", "recent"],
              "buttons": { "c1": { "label": "One", "text": "1" } } }
            """);
        Assert.Equal(["c1", "recent"], custom.Menu);
        Assert.Empty(custom.Warnings);
    }

    [Fact]
    public void More_takes_a_style_override_like_any_button()
    {
        var m = ToolbarModel.Resolve("""{ "version": 2, "items": { "left": ["pin", "more"] }, "overrides": { "more": { "size": "wide", "outline": true } } }""");
        Assert.Empty(m.Warnings);
        Assert.Equal(80, ToolbarPlan.Metrics(m, "more").Width);
        AssertValid(m);
    }

    // ---- planning ----

    [Fact]
    public void A_row_with_more_has_no_second_menu_button_and_merges_its_overflow_after_the_user_menu()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 2,
              "items": { "left": ["pin", "captureWindow", "captureScreen", "captureRegion", "prompt", "recent", "continueIn"], "right": ["settings", "more"] },
              "menu": ["newChat"] }
            """);
        var wide = ToolbarPlan.PlanRow(m, top: false, 2000, Spacing);
        Assert.True(wide.Plan.HasMore);
        Assert.Empty(wide.Dropped);
        Assert.Equal(["newChat"], wide.Menu);
        Assert.True(wide.Fit.Overflow); // the more button is always there
        Assert.Contains("more", wide.RightShown);
        var narrow = ToolbarPlan.PlanRow(m, top: false, 320, Spacing);
        Assert.NotEmpty(narrow.Dropped);
        Assert.Equal(["newChat", .. narrow.Dropped, .. narrow.RightDropped], narrow.Menu);
        Assert.Contains("more", narrow.RightShown);
        Assert.DoesNotContain("more", narrow.Menu);
        Assert.True(narrow.Used <= 320 + Eps, $"{narrow.Used}");
        // The other row has no more button: it keeps its own automatic one.
        Assert.False(ToolbarPlan.PlanRow(m, top: true, 320, Spacing).Plan.HasMore);
    }

    [Fact]
    public void Menu_entries_are_hidden_when_a_model_built_in_memory_has_no_more_button()
    {
        var m = ToolbarModel.Defaults with { Menu = ["recent"] };
        Assert.Empty(ToolbarPlan.MenuItems(m));
        Assert.Empty(ToolbarPlan.PlanRow(m, top: false, 1000, Spacing).Menu);
        var withMore = ToolbarModel.Defaults with
        {
            Placed = ToolbarModel.Defaults.Placed.ToDictionary(p => p.Key, p => p.Key == Quadrant.BottomRight ? ["more"] : p.Value),
            Menu = ["recent", "effort", "more", "pin"],
        };
        Assert.Empty(ToolbarPlan.MenuItems(withMore).Where(id => id is "effort" or "more" or "pin" or "recent")); // pin and recent are on the bar; pickers and more never
    }

    [Theory]
    [InlineData("more-menu-bottom-right-with-5-items")]
    [InlineData("more-menu-top-centre-with-overflow")]
    [InlineData("more-in-both-rows")]
    public void The_more_profiles_resolve_cleanly_and_keep_the_more_button_at_every_width_and_size(string name)
    {
        foreach (var size in Enum.GetValues<ButtonSize>())
        {
            var m = ToolbarModel.Resolve(ToolbarProfiles.Named(name).Json);
            Assert.Empty(m.Warnings);
            m = m with { Style = m.Style with { Size = size } };
            foreach (var top in new[] { true, false })
            {
                var row = ToolbarPlan.Row(m, top);
                int? lastDropped = null;
                for (double room = 320; room <= 1800; room += 7)
                {
                    var f = ToolbarPlan.PlanRow(m, top, room, Spacing);
                    if (!row.HasMore) { Assert.Empty(f.UserMenu); continue; }
                    var where = $"{name} {size} top {top} room {room}";
                    Assert.Contains("more", f.Shown.Concat(f.RightShown));
                    Assert.DoesNotContain("more", f.Menu);
                    Assert.True(f.Fit.Overflow, where);
                    Assert.Equal(ToolbarPlan.MenuItems(m), f.UserMenu);
                    Assert.Equal(f.UserMenu.Concat(f.Dropped).Concat(f.RightDropped), f.Menu);
                    Assert.DoesNotContain(f.Menu, ToolbarPlan.IsPicker);
                    Assert.Contains("more", f.ButtonWidths.Keys);
                    var dropped = f.Dropped.Count + f.RightDropped.Count;
                    if (lastDropped is { } before) Assert.True(dropped <= before, $"{where}: more room dropped more");
                    lastDropped = dropped;
                    // Within the room, except for the documented overshoot (not even one right button and the more button fit).
                    var moreWidth = ToolbarPlan.Metrics(m, "more").Width;
                    if (f.Used > room + Eps) Assert.True(f.Fit.LeftShown == 0 && moreWidth + Spacing * 2 > room - 40, $"{where}: {f.Used}");
                }
            }
        }
    }

    [Fact]
    public void More_in_a_row_that_cannot_hold_even_it_is_kept_as_the_overshoot()
    {
        var m = Read("\"pin\", \"prompt\"", "\"more\"", "");
        var f = ToolbarPlan.PlanRow(m, top: false, 10, Spacing);
        Assert.Equal(["pin", "prompt"], f.Dropped);
        Assert.Contains("more", f.RightShown);
    }

    // ---- editing ----

    private static ToolbarModel WithMore() =>
        ToolbarEditor.Show(ToolbarModel.Defaults, "more", Quadrant.BottomRight);

    [Fact]
    public void MoveToMenu_puts_a_bar_item_at_the_menu_and_back_to_a_group_takes_it_out()
    {
        var m = WithMore();
        var one = ToolbarEditor.MoveToMenu(m, "recent", int.MaxValue);
        AssertEdit(m, one);
        Assert.Equal(["recent"], one.Menu);
        Assert.DoesNotContain("recent", one.Items(Quadrant.BottomLeft));
        Assert.DoesNotContain("recent", ToolbarEditor.Hidden(one));
        var two = ToolbarEditor.DropToMenu(one, "pin", 0);
        AssertEdit(one, two);
        Assert.Equal(["pin", "recent"], two.Menu);
        var back = ToolbarEditor.MoveTo(two, "recent", Quadrant.TopRight, 0);
        AssertEdit(two, back);
        Assert.Equal(["pin"], back.Menu);
        Assert.Equal(["recent"], back.Items(Quadrant.TopRight));
        var shown = ToolbarEditor.Show(two, "pin", Quadrant.TopLeft);
        Assert.Equal(["recent"], shown.Menu);
        Assert.Contains("pin", shown.Items(Quadrant.TopLeft));
        AssertValid(shown);
    }

    [Fact]
    public void MoveToMenu_from_Not_shown_and_with_no_more_button_places_one_at_the_end_of_bottom_right()
    {
        var hidden = ToolbarEditor.Hide(ToolbarModel.Defaults, "newChat");
        var m = ToolbarEditor.MoveToMenu(hidden, "newChat", 0);
        AssertEdit(hidden, m);
        Assert.Equal(["newChat"], m.Menu);
        Assert.Equal(["more"], m.Items(Quadrant.BottomRight));
        Assert.DoesNotContain("newChat", ToolbarEditor.Hidden(m));
    }

    [Fact]
    public void MoveToMenu_refuses_pickers_more_and_unknown_ids()
    {
        var m = WithMore();
        Assert.Same(m, ToolbarEditor.MoveToMenu(m, "effort", 0));
        Assert.Same(m, ToolbarEditor.MoveToMenu(m, "more", 0));
        Assert.Same(m, ToolbarEditor.MoveToMenu(m, "nobody", 0));
        Assert.Same(m, ToolbarEditor.MoveToMenu(m, null!, 0));
        var inMenu = ToolbarEditor.MoveToMenu(m, "recent", 0);
        Assert.Same(inMenu, ToolbarEditor.MoveToMenu(inMenu, "recent", 0)); // already first: nothing changes
    }

    [Fact]
    public void Move_reorders_inside_the_menu_and_Hide_takes_an_entry_out_of_it()
    {
        var m = ToolbarEditor.MoveToMenu(ToolbarEditor.MoveToMenu(ToolbarEditor.MoveToMenu(WithMore(), "pin", int.MaxValue), "recent", int.MaxValue), "newChat", int.MaxValue);
        Assert.Equal(["pin", "recent", "newChat"], m.Menu);
        var moved = ToolbarEditor.Move(m, "newChat", -2);
        AssertEdit(m, moved);
        Assert.Equal(["newChat", "pin", "recent"], moved.Menu);
        var hidden = ToolbarEditor.Hide(moved, "pin");
        AssertEdit(moved, hidden);
        Assert.Equal(["newChat", "recent"], hidden.Menu);
        Assert.Contains("pin", ToolbarEditor.Hidden(hidden));
    }

    [Fact]
    public void Hiding_more_sends_the_menu_back_to_Not_shown()
    {
        var m = ToolbarEditor.MoveToMenu(ToolbarEditor.MoveToMenu(WithMore(), "pin", 0), "recent", 1);
        var h = ToolbarEditor.Hide(m, "more");
        AssertEdit(m, h);
        Assert.Empty(h.Menu);
        Assert.Contains("pin", ToolbarEditor.Hidden(h));
        Assert.Contains("recent", ToolbarEditor.Hidden(h));
        Assert.Contains("more", ToolbarEditor.Hidden(h));
    }

    [Fact]
    public void An_override_is_dropped_when_an_item_goes_into_the_menu_and_a_removed_custom_button_leaves_it()
    {
        var m = ToolbarEditor.SetStyle(WithMore(), "pin", new ButtonStyle(Outline: true));
        var inMenu = ToolbarEditor.MoveToMenu(m, "pin", 0);
        AssertEdit(m, inMenu);
        Assert.DoesNotContain("pin", inMenu.Overrides.Keys);
        var withCustom = ToolbarEditor.AddCustom(inMenu, new CustomButton("c1", "One", "E8A5", null, "t", false));
        var custom = ToolbarEditor.MoveToMenu(withCustom, "c1", 0);
        Assert.Equal(["c1", "pin"], custom.Menu);
        var removed = ToolbarEditor.RemoveCustom(custom, "c1");
        Assert.Equal(["pin"], removed.Menu);
        AssertValid(removed);
    }

    [Fact]
    public void Editor_view_lists_the_menu_units_and_who_may_go_into_it()
    {
        var m = ToolbarEditor.MoveToMenu(ToolbarEditor.MoveToMenu(WithMore(), "pin", 0), "recent", 1);
        var units = ToolbarEditorView.MenuUnits(m);
        Assert.Equal(["pin", "recent"], units.Select(u => u.Id));
        Assert.Equal((false, true), (units[0].CanMoveLeft, units[0].CanMoveRight));
        Assert.Equal((true, false), (units[1].CanMoveLeft, units[1].CanMoveRight));
        Assert.True(ToolbarEditorView.CanMoveToMenu(m, "newChat"));
        Assert.False(ToolbarEditorView.CanMoveToMenu(m, "pin")); // already in it
        Assert.False(ToolbarEditorView.CanMoveToMenu(m, "effort"));
        Assert.False(ToolbarEditorView.CanMoveToMenu(m, "more"));
        Assert.Empty(ToolbarEditorView.MenuUnits(ToolbarModel.Defaults));
    }

    // ---- fill the menu from the overflow ----

    [Fact]
    public void FillMenuFromOverflow_moves_what_the_planner_drops_into_the_menu_in_bar_order()
    {
        var m = WithMore();
        const double room = 320;
        var expected = ToolbarPlan.PlanRow(m, top: false, room, 2).Menu;
        Assert.NotEmpty(expected);
        var filled = ToolbarEditor.FillMenuFromOverflow(m, room, room);
        AssertEdit(m, filled);
        Assert.Equal(expected, filled.Menu);
        Assert.All(expected, id => Assert.DoesNotContain(id, filled.Items(Quadrant.BottomLeft)));
        // Everything that fits stays: nothing more is dropped by the planner now.
        Assert.Empty(ToolbarPlan.PlanRow(filled, top: false, room, 2).Dropped);
    }

    [Fact]
    public void FillMenuFromOverflow_places_a_more_button_first_when_there_is_none_and_does_nothing_when_all_fits()
    {
        var m = ToolbarModel.Defaults;
        var filled = ToolbarEditor.FillMenuFromOverflow(m, 600, 320);
        AssertEdit(m, filled);
        Assert.Equal("more", filled.Items(Quadrant.BottomRight)[^1]);
        Assert.NotEmpty(filled.Menu);
        var roomy = WithMore();
        Assert.Same(roomy, ToolbarEditor.FillMenuFromOverflow(roomy, 5000, 5000));
        Assert.Same(m, ToolbarEditor.FillMenuFromOverflow(m, 5000, 5000)); // nothing to move, so no more button is added either
    }
}
