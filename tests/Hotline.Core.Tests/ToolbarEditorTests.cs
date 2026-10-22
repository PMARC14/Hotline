using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ToolbarEditorTests
{
    private const Quadrant BL = Quadrant.BottomLeft, BR = Quadrant.BottomRight, TL = Quadrant.TopLeft, TR = Quadrant.TopRight;

    /// <summary>Three items in each of the six areas, no pickers shown (they are hidden).</summary>
    private static readonly ToolbarModel Grid = ToolbarModel.Resolve("""
        {
          "version": 2,
          "items": { "left": ["pin", "captureWindow", "captureScreen"], "center": ["ca", "cb", "cc"], "right": ["captureRegion", "prompt", "recent"] },
          "top":   { "left": ["continueIn", "newChat", "settings"], "center": ["cd", "ce", "cf"], "right": ["one", "two", "three"] },
          "buttons": {
            "one":   { "label": "One", "icon": "E8A5", "text": "1" },
            "two":   { "label": "Two", "icon": "E8A5", "text": "2" },
            "three": { "label": "Three", "icon": "E8A5", "quickAction": "three" },
            "ca":  { "label": "Four", "icon": "E8A5", "text": "4" },
            "cb":  { "label": "Five", "icon": "E8A5", "text": "5" },
            "cc":   { "label": "Six", "icon": "E8A5", "text": "6" },
            "cd": { "label": "Seven", "icon": "E8A5", "text": "7" },
            "ce": { "label": "Eight", "icon": "E8A5", "text": "8" },
            "cf":  { "label": "Nine", "icon": "E8A5", "text": "9" }
          }
        }
        """);

    private static ToolbarModel WithTopLeft(params string[] items) => ToolbarModel.Resolve(
        $$"""{ "version": 2, "items": { "left": ["pin"], "right": [] }, "top": { "left": [{{string.Join(", ", items.Select(i => $"\"{i}\""))}}], "right": ["recent"] } }""");

    private static CustomButton Text(string id, string label = "Label", string icon = "E8A5") => new(id, label, icon, null, "hello", false);

    private static void AssertValid(ToolbarModel m) => Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));

    private static List<string> With(IReadOnlyList<string> items, int index, string id)
    {
        var l = items.ToList();
        l.Insert(index, id);
        return l;
    }

    // ---- Move ----

    [Theory]
    [InlineData(Quadrant.BottomLeft)]
    [InlineData(Quadrant.BottomCenter)]
    [InlineData(Quadrant.BottomRight)]
    [InlineData(Quadrant.TopLeft)]
    [InlineData(Quadrant.TopCenter)]
    [InlineData(Quadrant.TopRight)]
    public void Move_reorders_within_the_quadrant_and_stops_at_the_edges(Quadrant q)
    {
        var (a, b, c) = (Grid.Items(q)[0], Grid.Items(q)[1], Grid.Items(q)[2]);
        Assert.Equal([b, a, c], ToolbarEditor.Move(Grid, b, -1).Items(q));
        Assert.Equal([a, c, b], ToolbarEditor.Move(Grid, b, 1).Items(q));
        Assert.Equal([b, c, a], ToolbarEditor.Move(Grid, a, 2).Items(q));
        Assert.Equal([b, c, a], ToolbarEditor.Move(Grid, a, 99).Items(q)); // past the end: clamped
        Assert.Equal([c, a, b], ToolbarEditor.Move(Grid, c, int.MinValue).Items(q));
        Assert.Same(Grid, ToolbarEditor.Move(Grid, a, -1)); // first, moving left: nothing to do
        Assert.Same(Grid, ToolbarEditor.Move(Grid, c, 1));  // last, moving right
        Assert.Same(Grid, ToolbarEditor.Move(Grid, b, 0));
        foreach (var other in Enum.GetValues<Quadrant>().Where(o => o != q))
            Assert.Equal(Grid.Items(other), ToolbarEditor.Move(Grid, b, -1).Items(other));
    }

    [Fact]
    public void Move_of_a_hidden_or_unknown_id_returns_the_input()
    {
        Assert.Same(Grid, ToolbarEditor.Move(Grid, "model", 1));
        Assert.Same(Grid, ToolbarEditor.Move(Grid, "nope", 1));
        Assert.Same(Grid, ToolbarEditor.Move(Grid, null!, 1));
        Assert.Same(Grid, ToolbarEditor.Move(Grid, "", 1));
    }

    [Fact]
    public void Ids_match_case_insensitively_and_keep_their_spelling()
    {
        var m = ToolbarEditor.Move(Grid, "CAPTUREWINDOW", -1);
        Assert.Equal(["captureWindow", "pin", "captureScreen"], m.Items(BL));
    }

    // ---- MoveTo (drag and drop) ----

    public static TheoryData<Quadrant, Quadrant, int> Pairs()
    {
        var data = new TheoryData<Quadrant, Quadrant, int>();
        foreach (var from in Enum.GetValues<Quadrant>())
            foreach (var to in Enum.GetValues<Quadrant>().Where(t => t != from))
                foreach (var index in new[] { 0, 1, 3, 99, -5 })
                    data.Add(from, to, index);
        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void MoveTo_between_quadrants_inserts_at_the_clamped_index(Quadrant from, Quadrant to, int index)
    {
        var id = Grid.Items(from)[1];
        var m = ToolbarEditor.MoveTo(Grid, id, to, index);
        Assert.Equal(With(Grid.Items(to), Math.Clamp(index, 0, 3), id), m.Items(to));
        Assert.Equal(Grid.Items(from).Where(i => i != id), m.Items(from));
        foreach (var other in Enum.GetValues<Quadrant>().Where(o => o != from && o != to))
            Assert.Equal(Grid.Items(other), m.Items(other));
        AssertValid(m);
    }

    [Theory]
    [InlineData(Quadrant.BottomLeft)]
    [InlineData(Quadrant.BottomCenter)]
    [InlineData(Quadrant.BottomRight)]
    [InlineData(Quadrant.TopLeft)]
    [InlineData(Quadrant.TopCenter)]
    [InlineData(Quadrant.TopRight)]
    public void MoveTo_within_a_quadrant_uses_the_drop_marker_position(Quadrant q)
    {
        var (a, b, c) = (Grid.Items(q)[0], Grid.Items(q)[1], Grid.Items(q)[2]);
        // The index is a gap in the current list (0 = before the first, Count = after the last).
        Assert.Same(Grid, ToolbarEditor.MoveTo(Grid, a, q, 0)); // onto its own place
        Assert.Same(Grid, ToolbarEditor.MoveTo(Grid, a, q, 1)); // the gap right after it: the same place
        Assert.Equal([b, a, c], ToolbarEditor.MoveTo(Grid, a, q, 2).Items(q));
        Assert.Equal([b, c, a], ToolbarEditor.MoveTo(Grid, a, q, 3).Items(q));
        Assert.Equal([b, c, a], ToolbarEditor.MoveTo(Grid, a, q, 99).Items(q));
        Assert.Equal([c, a, b], ToolbarEditor.MoveTo(Grid, c, q, 0).Items(q));
        Assert.Equal([a, c, b], ToolbarEditor.MoveTo(Grid, c, q, 1).Items(q));
        Assert.Same(Grid, ToolbarEditor.MoveTo(Grid, c, q, 3));
    }

    [Fact]
    public void MoveTo_of_a_hidden_item_places_it_like_a_drag_from_not_shown()
    {
        var m = ToolbarEditor.MoveTo(Grid, "model", BR, 1);
        Assert.Equal(["captureRegion", "model", "prompt", "recent"], m.Items(BR));
        AssertValid(m);
    }

    [Fact]
    public void MoveTo_with_bad_input_returns_the_input()
    {
        Assert.Same(Grid, ToolbarEditor.MoveTo(Grid, "nope", BR, 0));
        Assert.Same(Grid, ToolbarEditor.MoveTo(Grid, "pin", (Quadrant)7, 0));
        Assert.Same(Grid, ToolbarEditor.MoveTo(Grid, null!, BR, 0));
    }

    // ---- ShiftTo (arrows) ----

    [Theory]
    [InlineData(Quadrant.BottomLeft, Shift.Left, null)]
    [InlineData(Quadrant.BottomLeft, Shift.Right, Quadrant.BottomCenter)]
    [InlineData(Quadrant.BottomLeft, Shift.Up, Quadrant.TopLeft)]
    [InlineData(Quadrant.BottomLeft, Shift.Down, null)]
    [InlineData(Quadrant.BottomCenter, Shift.Left, Quadrant.BottomLeft)]
    [InlineData(Quadrant.BottomCenter, Shift.Right, Quadrant.BottomRight)]
    [InlineData(Quadrant.BottomCenter, Shift.Up, Quadrant.TopCenter)]
    [InlineData(Quadrant.BottomCenter, Shift.Down, null)]
    [InlineData(Quadrant.BottomRight, Shift.Left, Quadrant.BottomCenter)]
    [InlineData(Quadrant.BottomRight, Shift.Right, null)]
    [InlineData(Quadrant.BottomRight, Shift.Up, Quadrant.TopRight)]
    [InlineData(Quadrant.BottomRight, Shift.Down, null)]
    [InlineData(Quadrant.TopLeft, Shift.Left, null)]
    [InlineData(Quadrant.TopLeft, Shift.Right, Quadrant.TopCenter)]
    [InlineData(Quadrant.TopLeft, Shift.Up, null)]
    [InlineData(Quadrant.TopLeft, Shift.Down, Quadrant.BottomLeft)]
    [InlineData(Quadrant.TopCenter, Shift.Left, Quadrant.TopLeft)]
    [InlineData(Quadrant.TopCenter, Shift.Right, Quadrant.TopRight)]
    [InlineData(Quadrant.TopCenter, Shift.Up, null)]
    [InlineData(Quadrant.TopCenter, Shift.Down, Quadrant.BottomCenter)]
    [InlineData(Quadrant.TopRight, Shift.Left, Quadrant.TopCenter)]
    [InlineData(Quadrant.TopRight, Shift.Right, null)]
    [InlineData(Quadrant.TopRight, Shift.Up, null)]
    [InlineData(Quadrant.TopRight, Shift.Down, Quadrant.BottomRight)]
    public void ShiftTo_moves_to_the_end_of_the_neighbouring_quadrant_or_stops_at_the_edge(Quadrant from, Shift direction, Quadrant? to)
    {
        var id = Grid.Items(from)[0];
        var m = ToolbarEditor.ShiftTo(Grid, id, direction);
        if (to is not { } target) { Assert.Same(Grid, m); return; }
        Assert.Equal(Grid.Items(target).Append(id), m.Items(target));
        Assert.Equal(Grid.Items(from).Skip(1), m.Items(from));
        AssertValid(m);
    }

    [Fact]
    public void ShiftTo_of_a_hidden_item_or_a_bad_direction_returns_the_input()
    {
        Assert.Same(Grid, ToolbarEditor.ShiftTo(Grid, "model", Shift.Down));
        Assert.Same(Grid, ToolbarEditor.ShiftTo(Grid, "pin", (Shift)9));
    }

    // ---- The pickers are items like any other ----

    [Fact]
    public void Dragging_one_picker_moves_only_that_picker()
    {
        var m = WithTopLeft("provider", "effort", "model");
        var moved = ToolbarEditor.MoveTo(m, "model", BL, 0);
        Assert.Equal(["model", "pin"], moved.Items(BL));
        Assert.Equal(["provider", "effort"], moved.Items(TL));
        AssertValid(moved);

        moved = ToolbarEditor.MoveTo(m, "effort", TR, 1);
        Assert.Equal(["recent", "effort"], moved.Items(TR));
        Assert.Equal(["provider", "model"], moved.Items(TL));
        AssertValid(moved);
    }

    [Fact]
    public void A_drop_inside_its_own_quadrant_moves_one_picker_past_other_items_and_pickers()
    {
        var m = WithTopLeft("continueIn", "effort", "model", "provider", "newChat");
        Assert.Equal(["continueIn", "effort", "provider", "newChat", "model"], ToolbarEditor.MoveTo(m, "model", TL, 5).Items(TL));
        Assert.Equal(["provider", "continueIn", "effort", "model", "newChat"], ToolbarEditor.MoveTo(m, "provider", TL, 0).Items(TL));
        Assert.Equal(["continueIn", "model", "effort", "provider", "newChat"], ToolbarEditor.MoveTo(m, "model", TL, 1).Items(TL)); // between two buttons
        Assert.Same(m, ToolbarEditor.MoveTo(m, "model", TL, 2)); // right before itself
        Assert.Same(m, ToolbarEditor.MoveTo(m, "model", TL, 3)); // right after itself
    }

    [Fact]
    public void Move_and_ShiftTo_of_a_picker_carry_that_picker_only()
    {
        var m = WithTopLeft("continueIn", "effort", "model", "provider", "newChat");
        Assert.Equal(["continueIn", "effort", "provider", "model", "newChat"], ToolbarEditor.Move(m, "model", 1).Items(TL));
        Assert.Equal(["continueIn", "model", "effort", "provider", "newChat"], ToolbarEditor.Move(m, "model", -1).Items(TL));
        Assert.Equal(["provider", "continueIn", "effort", "model", "newChat"], ToolbarEditor.Move(m, "provider", -9).Items(TL)); // clamped
        Assert.Same(m, ToolbarEditor.Move(m, "continueIn", -1));
        var shifted = ToolbarEditor.ShiftTo(m, "provider", Shift.Right); // one column: the centre
        Assert.Equal(["provider"], shifted.Items(Quadrant.TopCenter));
        Assert.Equal(["continueIn", "effort", "model", "newChat"], shifted.Items(TL));
        Assert.Equal(["recent"], shifted.Items(TR));
        shifted = ToolbarEditor.ShiftTo(shifted, "provider", Shift.Right); // and the next one: the right
        Assert.Equal(["recent", "provider"], shifted.Items(TR));
        Assert.Empty(shifted.Items(Quadrant.TopCenter));
        shifted = ToolbarEditor.ShiftTo(m, "model", Shift.Down);
        Assert.Equal(["pin", "model"], shifted.Items(BL));
        Assert.Equal(["continueIn", "effort", "provider", "newChat"], shifted.Items(TL));
        AssertValid(shifted);
    }

    [Fact]
    public void Hiding_one_picker_leaves_the_others_and_showing_it_puts_it_at_the_end_of_the_chosen_area()
    {
        var m = WithTopLeft("continueIn", "effort", "model", "provider", "newChat");
        var hidden = ToolbarEditor.Hide(m, "model");
        Assert.Equal(["continueIn", "effort", "provider", "newChat"], hidden.Items(TL));
        Assert.Contains("model", ToolbarEditor.Hidden(hidden));
        AssertValid(hidden);

        var shown = ToolbarEditor.Show(hidden, "model", BR);
        Assert.Equal(["model"], shown.Items(BR));
        Assert.Equal(["continueIn", "effort", "provider", "newChat"], shown.Items(TL)); // the others stay
        AssertValid(shown);

        shown = ToolbarEditor.Show(hidden, "model", TL);
        Assert.Equal(["continueIn", "effort", "provider", "newChat", "model"], shown.Items(TL));
        AssertValid(shown);
    }

    [Fact]
    public void Each_picker_can_go_to_a_different_area_and_the_span_stays_with_it()
    {
        var m = ToolbarEditor.SetPickerSpan(WithTopLeft("effort", "model", "provider"), "model", 6);
        m = ToolbarEditor.MoveTo(m, "model", Quadrant.BottomCenter, 0);
        m = ToolbarEditor.MoveTo(m, "provider", BR, 0);
        Assert.Equal(["effort"], m.Items(TL));
        Assert.Equal(["model"], m.Items(Quadrant.BottomCenter));
        Assert.Equal(["provider"], m.Items(BR));
        Assert.Equal(6, m.EffectiveSpan("model"));
        AssertValid(m);
        Assert.Equal(6, ToolbarEditor.Hide(m, "model").EffectiveSpan("model")); // a hidden picker keeps its span
    }

    [Fact]
    public void Drop_places_a_hidden_picker_between_two_buttons()
    {
        var m = ToolbarEditor.Drop(Grid, "effort", BL, 1);
        Assert.Equal(["pin", "effort", "captureWindow", "captureScreen"], m.Items(BL));
        AssertValid(m);
    }

    [Fact]
    public void Every_picker_can_be_hidden_and_the_bar_can_be_emptied()
    {
        var m = ToolbarModel.Defaults;
        foreach (var id in ToolbarModel.Defaults.Placed.Values.SelectMany(l => l).ToList()) m = ToolbarEditor.Hide(m, id);
        Assert.All(Enum.GetValues<Quadrant>(), q => Assert.Empty(m.Items(q)));
        Assert.Equal(ToolbarModel.BuiltIn, ToolbarEditor.Hidden(m));
        AssertValid(m); // an explicitly empty bar stays empty
    }

    // ---- Hide / Show / Hidden ----

    [Fact]
    public void Hide_then_Show_puts_the_item_at_the_end_of_the_chosen_quadrant()
    {
        var m = ToolbarEditor.Hide(Grid, "pin");
        Assert.Equal(["captureWindow", "captureScreen"], m.Items(BL));
        Assert.Equal(["pin", "effort", "model", "provider", "more"], ToolbarEditor.Hidden(m)); // the more button is not placed by default
        foreach (var q in Enum.GetValues<Quadrant>())
        {
            var shown = ToolbarEditor.Show(m, "pin", q);
            Assert.Equal(m.Items(q).Append("pin"), shown.Items(q));
            AssertValid(shown);
        }
    }

    [Fact]
    public void Hide_or_Show_that_changes_nothing_returns_the_input()
    {
        Assert.Same(Grid, ToolbarEditor.Hide(Grid, "model"));    // already hidden
        Assert.Same(Grid, ToolbarEditor.Hide(Grid, "nope"));
        Assert.Same(Grid, ToolbarEditor.Show(Grid, "pin", TR));  // already shown: Show doesn't move (MoveTo does)
        Assert.Same(Grid, ToolbarEditor.Show(Grid, "nope", TR));
        Assert.Same(Grid, ToolbarEditor.Show(Grid, "effort", (Quadrant)(-1)));
    }

    [Fact]
    public void Hiding_a_styled_item_drops_its_style()
    {
        var styled = ToolbarEditor.SetStyle(Grid, "pin", new ButtonStyle(Outline: true));
        Assert.True(styled.Overrides.ContainsKey("pin"));
        var hidden = ToolbarEditor.Hide(styled, "pin");
        Assert.DoesNotContain("pin", hidden.Items(BL));
        Assert.False(hidden.Overrides.ContainsKey("pin"));
        Assert.False(ToolbarEditor.Show(hidden, "pin", BL).Overrides.ContainsKey("pin"));
    }

    [Fact]
    public void Hidden_lists_built_ins_in_their_order_then_custom_buttons_in_theirs()
    {
        var m = ToolbarModel.Resolve("""
            {
              "version": 2,
              "items": { "left": ["settings", "zeta", "pin"] },
              "buttons": {
                "zeta":  { "label": "Z", "text": "z" },
                "beta":  { "label": "B", "text": "b" },
                "alpha": { "label": "A", "text": "a" }
              }
            }
            """);
        Assert.Equal(ToolbarModel.BuiltIn.Where(id => id is not ("settings" or "pin")).Concat(["beta", "alpha"]), ToolbarEditor.Hidden(m));
    }

    // ---- Custom buttons ----

    [Fact]
    public void AddCustom_appends_the_button_and_places_it_at_the_end_of_bottom_left()
    {
        var m = ToolbarEditor.AddCustom(Grid, Text("four"));
        Assert.Equal(Text("four"), m.Buttons[^1]);
        Assert.Equal(Grid.Items(BL).Append("four"), m.Items(BL));
        AssertValid(m);
    }

    [Theory]
    [InlineData("pin")]          // built-in
    [InlineData("Pin")]          // built-in, other case
    [InlineData("one")]          // already a custom button
    [InlineData("One")]          // not a-z
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("abcdefghijabcdefghijabcdefghijabc")] // 33
    public void AddCustom_rejects_a_bad_id_and_returns_the_input(string id) =>
        Assert.Same(Grid, ToolbarEditor.AddCustom(Grid, Text(id)));

    [Fact]
    public void AddCustom_rejects_bad_buttons_and_returns_the_input()
    {
        Assert.Same(Grid, ToolbarEditor.AddCustom(Grid, null!));
        Assert.Same(Grid, ToolbarEditor.AddCustom(Grid, Text(null!)));
        Assert.Same(Grid, ToolbarEditor.AddCustom(Grid, new CustomButton("x", "X", "E8A5", "x", "both", false)));
        Assert.Same(Grid, ToolbarEditor.AddCustom(Grid, new CustomButton("x", "X", "E8A5", null, null, false)));
        Assert.Same(Grid, ToolbarEditor.AddCustom(Grid, new CustomButton("x", "X", "E8A5", null, "\u0001", false))); // no text left
        Assert.Same(Grid, ToolbarEditor.AddCustom(Grid, new CustomButton("x", "X", "E8A5", "..\\evil", null, false)));
        Assert.Same(Grid, ToolbarEditor.AddCustom(Grid, new CustomButton("x", "X", "E8A5", "c:evil", null, false)));
        Assert.Same(Grid, ToolbarEditor.AddCustom(Grid, new CustomButton("x", "X", "E8A5", new string('a', 65), null, false)));
    }

    [Fact]
    public void AddCustom_clamps_like_Resolve()
    {
        var m = ToolbarEditor.AddCustom(Grid, new CustomButton("x", " A\u0007" + new string('b', 60), "zzzz", null, new string('t', 3000), true));
        var b = m.Buttons.Single(b => b.Id == "x");
        Assert.Equal("A" + new string('b', 39), b.Label);
        Assert.Equal(ToolbarModel.DefaultIcon, b.Icon);
        Assert.Equal(2000, b.Text!.Length);
        Assert.True(b.Send);
        Assert.Empty(m.Warnings);
        AssertValid(m);

        Assert.Equal("x", ToolbarEditor.AddCustom(Grid, Text("x", label: "")).Buttons[^1].Label); // no label: the id
        Assert.Equal("F8FF", ToolbarEditor.AddCustom(Grid, Text("x", icon: "00f8ff")).Buttons[^1].Icon);
        Assert.False(ToolbarEditor.AddCustom(Grid, new CustomButton("x", "X", "E8A5", "x", null, true)).Buttons[^1].Send); // send is for text only
    }

    [Fact]
    public void AddCustom_never_throws_on_a_lone_surrogate()
    {
        var m = ToolbarEditor.AddCustom(Grid, Text("x", label: "bad \uD800 half"));
        Assert.Equal("bad � half", m.Buttons[^1].Label); // the writer replaces it
        AssertValid(m);
    }

    [Fact]
    public void AddCustom_stops_at_24_buttons()
    {
        var m = ToolbarModel.Defaults;
        for (var i = 0; i < 24; i++) m = ToolbarEditor.AddCustom(m, Text($"b{i}"));
        Assert.Equal(24, m.Buttons.Count);
        Assert.Same(m, ToolbarEditor.AddCustom(m, Text("b24")));
        Assert.Equal(9 + 24, m.Items(BL).Count);
        AssertValid(m);
    }

    [Fact]
    public void UpdateCustom_replaces_the_button_in_place_and_keeps_its_placement()
    {
        var updated = new CustomButton("two", "Second", "E8BD", "second", null, false);
        var m = ToolbarEditor.UpdateCustom(Grid, updated);
        Assert.Equal(Grid.Buttons.Select(b => b.Id == "two" ? updated : b), m.Buttons);
        Assert.Equal(Grid.Items(TR), m.Items(TR));
        AssertValid(m);
    }

    [Fact]
    public void UpdateCustom_with_an_unknown_or_invalid_button_returns_the_input()
    {
        Assert.Same(Grid, ToolbarEditor.UpdateCustom(Grid, Text("nope")));
        Assert.Same(Grid, ToolbarEditor.UpdateCustom(Grid, Text("pin")));
        Assert.Same(Grid, ToolbarEditor.UpdateCustom(Grid, null!));
        Assert.Same(Grid, ToolbarEditor.UpdateCustom(Grid, new CustomButton("two", "Two", "E8A5", null, null, false)));
        Assert.Same(Grid, ToolbarEditor.UpdateCustom(Grid, new CustomButton("two", "Two", "E8A5", "two", "both", false)));
        Assert.Same(Grid, ToolbarEditor.UpdateCustom(Grid, new CustomButton("two", "Two", "E8A5", "a/b", null, false)));
    }

    [Fact]
    public void RemoveCustom_clears_the_button_its_placement_and_its_override()
    {
        var styled = ToolbarEditor.SetStyle(Grid, "two", new ButtonStyle(Width: 30));
        Assert.True(styled.Overrides.ContainsKey("two"));
        var m = ToolbarEditor.RemoveCustom(styled, "two");
        Assert.DoesNotContain(m.Buttons, b => b.Id == "two");
        Assert.Equal(["one", "three"], m.Items(TR));
        Assert.False(m.Overrides.ContainsKey("two"));
        Assert.DoesNotContain("two", ToolbarEditor.Hidden(m));
        AssertValid(m);

        var hidden = ToolbarEditor.RemoveCustom(ToolbarEditor.Hide(Grid, "one"), "one");
        Assert.Equal(Grid.Buttons.Select(b => b.Id).Where(id => id != "one"), hidden.Buttons.Select(b => b.Id));
    }

    [Fact]
    public void Custom_edits_keep_or_clear_the_icon_file()
    {
        var added = ToolbarEditor.AddCustom(Grid, Text("four") with { IconFile = "four.png" });
        Assert.Equal("four.png", added.Buttons[^1].IconFile);
        AssertValid(added);

        var relabelled = ToolbarEditor.UpdateCustom(added, added.Buttons[^1] with { Label = "Four!" });
        Assert.Equal("four.png", relabelled.Buttons[^1].IconFile);
        var cleared = ToolbarEditor.UpdateCustom(relabelled, relabelled.Buttons[^1] with { IconFile = null });
        Assert.Null(cleared.Buttons[^1].IconFile);
        Assert.Equal("Four!", cleared.Buttons[^1].Label);
        var set = ToolbarEditor.UpdateCustom(Grid, Grid.Buttons[0] with { IconFile = "one.png" });
        Assert.Equal("one.png", set.Buttons[0].IconFile);
        AssertValid(set);

        Assert.DoesNotContain(ToolbarEditor.RemoveCustom(added, "four").Buttons, b => b.IconFile is not null);
    }

    [Fact]
    public void A_custom_button_never_keeps_another_buttons_or_a_path_icon_file()
    {
        foreach (var bad in new[] { "one.png", "../four.png", "four.PNG", "C:\\four.png" })
        {
            var m = ToolbarEditor.AddCustom(Grid, Text("four") with { IconFile = bad });
            Assert.Null(m.Buttons[^1].IconFile); // dropped like Resolve does; the glyph stays
            AssertValid(m);
            Assert.Same(Grid, ToolbarEditor.UpdateCustom(Grid, Grid.Buttons[1] with { IconFile = bad })); // nothing changes
        }
    }

    [Fact]
    public void RemoveCustom_of_a_built_in_or_unknown_id_returns_the_input()
    {
        Assert.Same(Grid, ToolbarEditor.RemoveCustom(Grid, "pin"));
        Assert.Same(Grid, ToolbarEditor.RemoveCustom(Grid, "nope"));
        Assert.Same(Grid, ToolbarEditor.RemoveCustom(Grid, null!));
    }

    // ---- Styles ----

    [Fact]
    public void SetStyle_for_the_bar_clamps_and_fills_the_defaults()
    {
        var m = ToolbarEditor.SetStyle(Grid, null, new ButtonStyle(ButtonSize.Wide, true, null, 500, 5));
        Assert.Equal(new ButtonStyle(ButtonSize.Wide, true, false, 80, 20), m.Style);
        Assert.Equal(new ButtonStyle(ButtonSize.Normal, false, false), ToolbarEditor.SetStyle(Grid, null, new ButtonStyle()).Style);
        Assert.Equal(new ButtonStyle(ButtonSize.Normal, false, false), ToolbarEditor.SetStyle(m, null, new ButtonStyle()).Style);
        Assert.Equal(new ButtonStyle(ButtonSize.Normal, false, false), ToolbarEditor.SetStyle(Grid, null, new ButtonStyle((ButtonSize)42)).Style);
        AssertValid(m);
    }

    [Fact]
    public void SetStyle_for_an_item_sets_clamps_and_clears_its_override()
    {
        var m = ToolbarEditor.SetStyle(Grid, "PIN", new ButtonStyle(Filled: true, Width: 100));
        Assert.Equal(new ButtonStyle(Filled: true, Width: 80), m.Overrides["pin"]);
        Assert.Equal(new ButtonStyle(ButtonSize.Normal, false, true, 80, null), m.EffectiveStyle("pin"));
        AssertValid(m);
        Assert.False(ToolbarEditor.SetStyle(m, "pin", new ButtonStyle()).Overrides.ContainsKey("pin"));
    }

    [Fact]
    public void SetStyle_takes_a_size_and_an_outline_color_for_the_bar_and_an_item()
    {
        var bar = ToolbarEditor.SetStyle(Grid, null, new ButtonStyle(ButtonSize.Wide, true, null, OutlineColor: "#e81123"));
        Assert.Equal(new ButtonStyle(ButtonSize.Wide, true, false, OutlineColor: "#E81123"), bar.Style);
        var item = ToolbarEditor.SetStyle(bar, "pin", new ButtonStyle(ButtonSize.Compact, OutlineColor: "#00B7C3"));
        Assert.Equal(new ButtonStyle(ButtonSize.Compact, true, false, OutlineColor: "#00B7C3"), item.EffectiveStyle("pin"));
        AssertValid(item);
        // An invalid colour is dropped (the item inherits the bar's), the rest of the edit stays.
        var bad = ToolbarEditor.SetStyle(bar, "pin", new ButtonStyle(Filled: true, OutlineColor: "red"));
        Assert.Equal(new ButtonStyle(Filled: true), bad.Overrides["pin"]);
        Assert.Equal("#E81123", bad.EffectiveStyle("pin").OutlineColor);
        // Clearing the bar's colour goes back to the accent (null); the same colour again changes nothing.
        Assert.Null(ToolbarEditor.SetStyle(bar, null, bar.Style with { OutlineColor = null }).Style.OutlineColor);
        Assert.Same(bar, ToolbarEditor.SetStyle(bar, null, bar.Style with { OutlineColor = "#e81123" }));
    }

    [Fact]
    public void SetBarSize_sizes_the_whole_bar_and_clears_only_the_items_sizes()
    {
        var m = ToolbarEditor.SetStyle(Grid, null, Grid.Style with { Outline = true, Width = 40 });
        m = ToolbarEditor.SetStyle(m, "pin", new ButtonStyle(ButtonSize.Compact, Filled: true));
        m = ToolbarEditor.SetStyle(m, "one", new ButtonStyle(ButtonSize.Wide, OutlineColor: "#E81123"));
        m = ToolbarEditor.SetStyle(m, "prompt", new ButtonStyle(ButtonSize.Wide));
        Assert.Equal(BarSizeChoice.Custom, ToolbarEditorView.BarSize(m));

        var wide = ToolbarEditor.SetBarSize(m, ButtonSize.Wide);
        Assert.Equal(new ButtonStyle(ButtonSize.Wide, true, false, 40), wide.Style); // the bar's other keys stay
        Assert.Equal(new ButtonStyle(Filled: true), wide.Overrides["pin"]);
        Assert.Equal(new ButtonStyle(OutlineColor: "#E81123"), wide.Overrides["one"]);
        Assert.False(wide.Overrides.ContainsKey("prompt")); // nothing left of it
        Assert.All(new[] { "pin", "one", "prompt", "recent" }, id => Assert.Equal(ButtonSize.Wide, wide.EffectiveStyle(id).Size));
        Assert.Equal(BarSizeChoice.Wide, ToolbarEditorView.BarSize(wide));
        AssertValid(wide);
        Assert.Empty(ToolbarModel.Resolve(wide.ToJson()).Warnings);
    }

    [Fact]
    public void SetBarSize_that_changes_nothing_or_is_invalid_returns_the_input()
    {
        var wide = ToolbarEditor.SetBarSize(Grid, ButtonSize.Wide);
        Assert.Same(wide, ToolbarEditor.SetBarSize(wide, ButtonSize.Wide));
        Assert.Same(Grid, ToolbarEditor.SetBarSize(Grid, ButtonSize.Normal)); // already normal, no item sizes
        Assert.Same(Grid, ToolbarEditor.SetBarSize(Grid, (ButtonSize)42));
        Assert.Null(ToolbarEditor.SetBarSize(null!, ButtonSize.Wide));
        // Same bar size but an item had its own: clearing it is a change.
        var mixed = ToolbarEditor.SetStyle(Grid, "pin", new ButtonStyle(ButtonSize.Compact));
        var cleared = ToolbarEditor.SetBarSize(mixed, ButtonSize.Normal);
        Assert.NotSame(mixed, cleared);
        Assert.Empty(cleared.Overrides);
    }

    [Fact]
    public void SetStyle_for_a_picker_a_hidden_or_unknown_item_returns_the_input()
    {
        var m = ToolbarModel.Defaults;
        Assert.Same(m, ToolbarEditor.SetStyle(m, "model", new ButtonStyle(Outline: true)));
        Assert.Same(Grid, ToolbarEditor.SetStyle(Grid, "model", new ButtonStyle(Outline: true))); // hidden
        Assert.Same(Grid, ToolbarEditor.SetStyle(Grid, "nope", new ButtonStyle(Outline: true)));
        Assert.Same(Grid, ToolbarEditor.SetStyle(Grid, "pin", null!));
        Assert.Same(Grid, ToolbarEditor.SetStyle(Grid, null, null!));
    }

    [Fact]
    public void A_null_model_is_returned_as_is()
    {
        Assert.Null(ToolbarEditor.Move(null!, "pin", 1));
        Assert.Null(ToolbarEditor.MoveTo(null!, "pin", BL, 0));
        Assert.Null(ToolbarEditor.ShiftTo(null!, "pin", Shift.Up));
        Assert.Null(ToolbarEditor.Hide(null!, "pin"));
        Assert.Null(ToolbarEditor.Show(null!, "pin", BL));
        Assert.Null(ToolbarEditor.AddCustom(null!, Text("x")));
        Assert.Null(ToolbarEditor.UpdateCustom(null!, Text("x")));
        Assert.Null(ToolbarEditor.RemoveCustom(null!, "x"));
        Assert.Null(ToolbarEditor.SetStyle(null!, null, new ButtonStyle()));
        Assert.Empty(ToolbarEditor.Hidden(null!));
    }

    // ---- Property: random edit sequences ----

    [Fact]
    public void Random_edit_sequences_always_give_a_valid_model_and_never_duplicate_or_lose_an_id()
    {
        var random = new Random(20261017);
        var changed = 0;
        string[] junk = ["", "nope", "PIN", "Model", "x y", "../a", "\uD800", "spacer", new string('z', 40)];
        // Quick action names: valid, then what QuickActions can't parse ("/x y" would run "x") or isn't a file name.
        string[] actionNames = ["act", "act", "x y", "a.b", "", new string('q', 65)];
        // Outline colours: valid (either case), then what the file can't hold (dropped on reading back).
        string?[] colors = [null, null, "#E81123", "#00b7c3", "#FFF", "#80E81123", "red", "", "#E8112", " #E81123"];
        var invalidActions = 0;
        for (var run = 0; run < 2000; run++)
        {
            var m = random.Next(3) switch { 0 => ToolbarModel.Defaults, 1 => Grid, _ => WithTopLeft("continueIn", "effort", "model", "provider") };
            for (var step = 0; step < 12; step++)
            {
                var known = ToolbarModel.BuiltIn.Concat(m.Buttons.Select(b => b.Id)).ToList();
                string Id() => random.Next(8) == 0 ? junk[random.Next(junk.Length)] : random.Next(10) == 0 ? $"c{random.Next(30)}" : known[random.Next(known.Count)];
                var q = (Quadrant)random.Next(-1, 5);
                var id = Id();
                var op = random.Next(10);
                var action = actionNames[random.Next(actionNames.Length)];
                var useAction = random.Next(2) == 0;
                ToolbarModel next = op switch
                {
                    0 => ToolbarEditor.Move(m, id, random.Next(-5, 6)),
                    1 => ToolbarEditor.MoveTo(m, id, q, random.Next(-2, 45)),
                    2 => ToolbarEditor.ShiftTo(m, id, (Shift)random.Next(-1, 5)),
                    3 => ToolbarEditor.Hide(m, id),
                    4 => ToolbarEditor.Show(m, id, q),
                    5 => ToolbarEditor.AddCustom(m, new CustomButton(id, junk[random.Next(junk.Length)], random.Next(2) == 0 ? "E8A5" : "zz",
                        useAction ? action : null, random.Next(3) == 0 ? null : "txt", random.Next(2) == 0)),
                    6 => ToolbarEditor.UpdateCustom(m, useAction
                        ? new CustomButton(id, "L", "E8BD", action, null, false, random.Next(3) == 0 ? id + ".png" : null)
                        : new CustomButton(id, "L", "E8BD", null, "t", true, random.Next(4) == 0 ? junk[random.Next(junk.Length)] + ".png" : null)),
                    7 => ToolbarEditor.RemoveCustom(m, id),
                    8 when random.Next(4) == 0 => ToolbarEditor.SetBarSize(m, (ButtonSize)random.Next(-1, 4)),
                    8 => ToolbarEditor.SetStyle(m, random.Next(3) == 0 ? null : id,
                        new ButtonStyle(random.Next(2) == 0 ? null : (ButtonSize)random.Next(7), null, random.Next(2) == 0, random.Next(-50, 200), null,
                            colors[random.Next(colors.Length)])),
                    _ => ToolbarEditor.Move(m, id, 0),
                };

                Assert.Equal(next, ToolbarModel.Resolve(next.ToJson()));
                if (op is 5 or 6 && useAction && action != "act")
                {
                    invalidActions++;
                    Assert.Same(m, next); // refused: the file could never hold it
                }
                var placed = Enum.GetValues<Quadrant>().SelectMany(next.Items).ToList();
                Assert.Equal(placed.Count, placed.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                var all = ToolbarModel.BuiltIn.Concat(next.Buttons.Select(b => b.Id)).ToList();
                Assert.Equal(all.Order(), placed.Concat(next.Menu).Concat(ToolbarEditor.Hidden(next)).Order());
                var before = Enum.GetValues<Quadrant>().SelectMany(m.Items);
                var lost = before.Except(placed).ToList();
                if (op is 3 or 7) Assert.True(lost.Count <= 1, $"run {run} step {step}: {op} lost {string.Join(",", lost)}");
                else Assert.Empty(lost);
                Assert.True(ReferenceEquals(next, m) || next != m, $"run {run} step {step}: op {op} returned an equal copy");
                if (!ReferenceEquals(next, m)) changed++;
                m = next;
            }
        }
        Assert.True(changed > 5000, $"only {changed} of 24000 edits changed the model");
        Assert.True(invalidActions > 500, $"only {invalidActions} edits tried an invalid quick action name");
    }
}
