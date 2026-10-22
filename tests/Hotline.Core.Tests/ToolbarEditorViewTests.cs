using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>The pure half of Settings' quadrant editor: the picker-block snap, drops, the strips' units and arrows,
/// custom button ids, the icon list and the checks behind its notices.</summary>
public sealed class ToolbarEditorViewTests
{
    private const Quadrant BL = Quadrant.BottomLeft, BR = Quadrant.BottomRight, TL = Quadrant.TopLeft, TR = Quadrant.TopRight;

    private static ToolbarModel Model(string bottomLeft, string topLeft, string topRight = "", string bottomRight = "", string buttons = "") =>
        ToolbarModel.Resolve($$"""
            { "version": 2, "items": { "left": [{{bottomLeft}}], "right": [{{bottomRight}}] }, "top": { "left": [{{topLeft}}], "right": [{{topRight}}] },
              "buttons": { {{buttons}} } }
            """);

    /// <summary>top left: continueIn, [effort, model, provider], newChat (the block at 1-3).</summary>
    private static readonly ToolbarModel Block = Model("\"pin\"", "\"continueIn\", \"effort\", \"model\", \"provider\", \"newChat\"", "\"recent\"");

    private static CustomButton Text(string id, string label = "Label") => new(id, label, "E8A5", null, "hello", false);

    // ---- SnapIndex ----

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)] // right before the block
    [InlineData(2, 1)] // inside, nearer the start
    [InlineData(3, 4)] // inside, nearer the end
    [InlineData(4, 4)] // right after the block
    [InlineData(5, 5)]
    [InlineData(99, 99)] // past the end: left to MoveTo's clamp
    [InlineData(-3, -3)]
    public void SnapIndex_moves_a_gap_inside_the_picker_block_to_its_nearer_edge(int index, int snapped) =>
        Assert.Equal(snapped, ToolbarEditor.SnapIndex(Block, TL, index));

    [Fact]
    public void SnapIndex_with_two_pickers_breaks_the_tie_towards_the_start()
    {
        var m = Model("\"pin\"", "\"continueIn\", \"effort\", \"model\"");
        Assert.Equal(1, ToolbarEditor.SnapIndex(m, TL, 2));
        Assert.Equal(3, ToolbarEditor.SnapIndex(m, TL, 3));
    }

    [Fact]
    public void SnapIndex_leaves_other_quadrants_one_picker_and_no_pickers_alone()
    {
        foreach (var q in new[] { BL, BR, TR })
            for (var i = 0; i < 5; i++) Assert.Equal(i, ToolbarEditor.SnapIndex(Block, q, i));
        var one = Model("\"pin\", \"model\", \"settings\"", "");
        for (var i = 0; i < 4; i++) Assert.Equal(i, ToolbarEditor.SnapIndex(one, BL, i));
        var none = Model("\"pin\", \"settings\"", "\"recent\"");
        for (var i = 0; i < 3; i++) Assert.Equal(i, ToolbarEditor.SnapIndex(none, BL, i));
        Assert.Equal(2, ToolbarEditor.SnapIndex(null!, BL, 2));
    }

    [Fact]
    public void A_snapped_drop_inside_the_block_is_kept_where_MoveTo_alone_refuses_it()
    {
        Assert.Same(Block, ToolbarEditor.MoveTo(Block, "pin", TL, 2)); // the editor refuses a split block
        Assert.Equal(["continueIn", "pin", "effort", "model", "provider", "newChat"],
            ToolbarEditor.MoveTo(Block, "pin", TL, ToolbarEditor.SnapIndex(Block, TL, 2)).Items(TL));
    }

    // ---- Drop ----

    [Fact]
    public void Drop_snaps_inside_the_block_and_places_the_item()
    {
        Assert.Equal(["continueIn", "effort", "model", "provider", "pin", "newChat"], ToolbarEditor.Drop(Block, "pin", TL, 3).Items(TL));
        Assert.Empty(ToolbarEditor.Drop(Block, "pin", TL, 3).Items(BL));
    }

    [Fact]
    public void Drop_of_a_hidden_item_shows_it_there()
    {
        var m = ToolbarEditor.Drop(Block, "settings", TR, 0);
        Assert.Equal(["settings", "recent"], m.Items(TR));
        Assert.DoesNotContain("settings", ToolbarEditor.Hidden(m));
    }

    [Fact]
    public void Drop_onto_its_own_place_or_of_an_unknown_id_changes_nothing()
    {
        Assert.Same(Block, ToolbarEditor.Drop(Block, "continueIn", TL, 0));
        Assert.Same(Block, ToolbarEditor.Drop(Block, "continueIn", TL, 1));
        Assert.Same(Block, ToolbarEditor.Drop(Block, "model", TL, 2)); // a picker inside its own block
        Assert.Same(Block, ToolbarEditor.Drop(Block, "nope", TL, 0));
        Assert.Same(Block, ToolbarEditor.Drop(Block, null!, TL, 0));
        Assert.Same(Block, ToolbarEditor.Drop(Block, "pin", (Quadrant)9, 0));
    }

    [Fact]
    public void Drop_never_throws_and_always_gives_a_valid_model()
    {
        var random = new Random(9);
        var ids = ToolbarModel.BuiltIn.Concat(["one", "nope", ""]).ToArray();
        var m = Model("\"pin\", \"one\"", "\"effort\", \"model\", \"provider\"", buttons: "\"one\": { \"label\": \"One\", \"text\": \"1\" }");
        for (var i = 0; i < 2000; i++)
        {
            m = ToolbarEditor.Drop(m, ids[random.Next(ids.Length)], (Quadrant)random.Next(-1, 5), random.Next(-2, 12));
            Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
            Assert.Equal(m.Placed.Values.SelectMany(v => v).Count(), m.Placed.Values.SelectMany(v => v).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    // ---- Units (the strips) ----

    [Fact]
    public void Units_show_each_item_alone_and_the_pickers_as_one_block_in_file_order()
    {
        var m = Model("\"pin\"", "\"continueIn\", \"provider\", \"effort\", \"newChat\"", buttons: "");
        var units = ToolbarEditorView.Units(m, TL);
        Assert.Equal(3, units.Count);
        Assert.Equal(["continueIn"], units[0].Ids);
        Assert.Equal(["provider", "effort"], units[1].Ids);
        Assert.True(units[1].IsPickers);
        Assert.Equal("provider", units[1].Id);
        Assert.False(units[0].IsPickers);
        Assert.Empty(ToolbarEditorView.Units(m, TR));
    }

    [Fact]
    public void Units_arrows_are_off_at_the_edges()
    {
        var units = ToolbarEditorView.Units(Block, TL);
        Assert.False(units[0].CanMoveLeft);
        Assert.True(units[0].CanMoveRight);
        Assert.True(units[1].CanMoveLeft && units[1].CanMoveRight);
        Assert.False(units[2].CanMoveRight);
        Assert.All(units, u => Assert.False(u.CanShiftUp));
        Assert.All(units, u => Assert.True(u.CanShiftDown));
        Assert.All(units, u => Assert.False(u.CanShiftLeft));
        Assert.All(units, u => Assert.True(u.CanShiftRight));
        var bottomRight = ToolbarEditorView.Units(Model("\"pin\"", "", bottomRight: "\"settings\""), BR).Single();
        Assert.Equal((false, false, true, false, true, false),
            (bottomRight.CanMoveLeft, bottomRight.CanMoveRight, bottomRight.CanShiftUp, bottomRight.CanShiftDown, bottomRight.CanShiftLeft, bottomRight.CanShiftRight));
    }

    [Fact]
    public void Every_enabled_arrow_changes_the_model_and_every_disabled_one_does_not()
    {
        var random = new Random(17);
        var ids = ToolbarModel.BuiltIn.Concat(["one", "two"]).ToArray();
        var m = Model("\"pin\", \"one\"", "\"effort\", \"model\", \"provider\"",
            buttons: "\"one\": { \"label\": \"One\", \"text\": \"1\" }, \"two\": { \"label\": \"Two\", \"quickAction\": \"two\" }");
        for (var round = 0; round < 300; round++)
        {
            m = random.Next(3) switch
            {
                0 => ToolbarEditor.Drop(m, ids[random.Next(ids.Length)], (Quadrant)random.Next(4), random.Next(0, 8)),
                1 => ToolbarEditor.Hide(m, ids[random.Next(ids.Length)]),
                _ => ToolbarEditor.ShiftTo(m, ids[random.Next(ids.Length)], (Shift)random.Next(4)),
            };
            foreach (var q in Enum.GetValues<Quadrant>())
                foreach (var u in ToolbarEditorView.Units(m, q))
                {
                    Assert.Equal(u.CanMoveLeft, !ReferenceEquals(m, ToolbarEditor.Move(m, u.Id, -1)));
                    Assert.Equal(u.CanMoveRight, !ReferenceEquals(m, ToolbarEditor.Move(m, u.Id, 1)));
                    Assert.Equal(u.CanShiftUp, !ReferenceEquals(m, ToolbarEditor.ShiftTo(m, u.Id, Shift.Up)));
                    Assert.Equal(u.CanShiftDown, !ReferenceEquals(m, ToolbarEditor.ShiftTo(m, u.Id, Shift.Down)));
                    Assert.Equal(u.CanShiftLeft, !ReferenceEquals(m, ToolbarEditor.ShiftTo(m, u.Id, Shift.Left)));
                    Assert.Equal(u.CanShiftRight, !ReferenceEquals(m, ToolbarEditor.ShiftTo(m, u.Id, Shift.Right)));
                }
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)] // before the block
    [InlineData(2, 4)] // after the block: past its three pickers
    [InlineData(3, 5)]
    [InlineData(9, 5)] // clamped
    [InlineData(-1, 0)]
    public void ItemIndex_turns_a_gap_between_units_into_a_gap_in_the_list(int unitGap, int index) =>
        Assert.Equal(index, ToolbarEditorView.ItemIndex(Block, TL, unitGap));

    [Fact]
    public void ItemIndex_after_the_block_lands_after_it()
    {
        var m = ToolbarEditor.Drop(Block, "pin", TL, ToolbarEditorView.ItemIndex(Block, TL, 2));
        Assert.Equal(["continueIn", "effort", "model", "provider", "pin", "newChat"], m.Items(TL));
    }

    // ---- Show (the Not shown strip's add button) ----

    [Fact]
    public void Show_puts_an_item_at_the_end_of_bottom_left()
    {
        var m = ToolbarEditorView.Show(Block, "settings");
        Assert.Equal(["pin", "settings"], m.Items(BL));
        Assert.Same(Block, ToolbarEditorView.Show(Block, "pin")); // already shown
        Assert.Same(Block, ToolbarEditorView.Show(Block, "nope"));
    }

    [Fact]
    public void Show_of_a_hidden_picker_joins_the_block_where_it_is()
    {
        var hidden = ToolbarEditor.Hide(Block, "effort");
        var m = ToolbarEditorView.Show(hidden, "effort");
        Assert.Equal(["continueIn", "model", "provider", "effort", "newChat"], m.Items(TL));
        var all = ToolbarEditor.Hide(ToolbarEditor.Hide(hidden, "model"), "provider");
        Assert.Equal(["pin", "model"], ToolbarEditorView.Show(all, "model").Items(BL)); // no block left: bottom left
    }

    // ---- Custom button ids ----

    [Theory]
    [InlineData("Summarise", "summarise")]
    [InlineData("  Say  thanks! ", "say-thanks")]
    [InlineData("Résumé für Ärzte", "resume-fur-arzte")]
    [InlineData("--a__b--", "a-b")]
    [InlineData("日本語", "button")]
    [InlineData("", "button")]
    [InlineData("Settings", "settings-2")] // a built-in, whatever the case
    [InlineData("NewChat", "newchat-2")]
    [InlineData("Spacer", "spacer-2")] // the old layout item of that name is reserved
    [InlineData("taken", "taken-2")]
    [InlineData("Taken too", "taken-too")]
    public void NewCustomId_is_a_valid_unused_id_from_the_label(string label, string id)
    {
        var m = ToolbarEditor.AddCustom(ToolbarModel.Defaults, Text("taken"));
        Assert.Equal(id, ToolbarEditorView.NewCustomId(m, label));
        Assert.NotSame(m, ToolbarEditor.AddCustom(m, Text(id)));
    }

    [Fact]
    public void NewCustomId_counts_up_and_stays_within_32_characters()
    {
        var m = ToolbarEditor.AddCustom(ToolbarModel.Defaults, Text("button"));
        m = ToolbarEditor.AddCustom(m, Text("button-2"));
        Assert.Equal("button-3", ToolbarEditorView.NewCustomId(m, "!!!"));
        var longLabel = new string('a', 40);
        var first = ToolbarEditorView.NewCustomId(m, longLabel);
        Assert.Equal(new string('a', 32), first);
        m = ToolbarEditor.AddCustom(m, Text(first));
        var second = ToolbarEditorView.NewCustomId(m, longLabel);
        Assert.Equal(new string('a', 30) + "-2", second);
        Assert.NotSame(m, ToolbarEditor.AddCustom(m, Text(second)));
        Assert.Equal("ab-2", ToolbarEditorView.NewCustomId(ToolbarEditor.AddCustom(m, Text("ab")), "AB"));
    }

    [Fact]
    public void NewCustomId_never_ends_in_a_dash_when_cut()
    {
        var label = new string('a', 31) + " b"; // "aaa…a-b" cut at 32 would end in "-"
        var id = ToolbarEditorView.NewCustomId(ToolbarModel.Defaults, label);
        Assert.Equal(new string('a', 31), id);
    }

    // ---- Icons and quick actions ----

    [Fact]
    public void Icons_are_sixteen_distinct_valid_codes_including_the_default()
    {
        Assert.Equal(16, ToolbarEditorView.Icons.Count);
        Assert.Equal(16, ToolbarEditorView.Icons.Distinct().Count());
        Assert.Contains(ToolbarModel.DefaultIcon, ToolbarEditorView.Icons);
        foreach (var icon in ToolbarEditorView.Icons)
        {
            var m = ToolbarEditor.AddCustom(ToolbarModel.Defaults, new CustomButton("x", "X", icon, null, "t", false));
            Assert.Equal(icon, m.Buttons.Single().Icon); // Resolve keeps it as it is
        }
    }

    [Fact]
    public void ActionChoices_are_valid_file_names_sorted_without_Hotlines_own_commands()
    {
        Assert.Equal(["fix", "Summarise", "translate-de"],
            ToolbarEditorView.ActionChoices(["translate-de", "Summarise", "a b", "../x", "remember", "fix", "FIX", new string('x', 65)], null));
        Assert.Equal(["fix", "gone"], ToolbarEditorView.ActionChoices(["fix"], "gone")); // the button's own, file missing
        Assert.Equal(["fix"], ToolbarEditorView.ActionChoices(["fix"], "Fix"));
        Assert.Equal(["fix"], ToolbarEditorView.ActionChoices(["fix"], "bad name"));
    }

    // ---- Checks behind the custom button notices ----

    [Fact]
    public void Check_names_what_is_missing()
    {
        var m = ToolbarModel.Defaults;
        Assert.Equal(CustomProblem.None, ToolbarEditorView.Check(m, Text("a"), adding: true));
        Assert.Equal(CustomProblem.NoLabel, ToolbarEditorView.Check(m, Text("a", "  "), adding: true));
        Assert.Equal(CustomProblem.NoAction, ToolbarEditorView.Check(m, new CustomButton("a", "A", "E734", null, " \n", false), adding: true));
        Assert.Equal(CustomProblem.NoAction, ToolbarEditorView.Check(m, new CustomButton("a", "A", "E734", null, null, false), adding: true));
        Assert.Equal(CustomProblem.NoAction, ToolbarEditorView.Check(m, new CustomButton("a", "A", "E734", "", null, false), adding: true));
        Assert.Equal(CustomProblem.BadQuickAction, ToolbarEditorView.Check(m, new CustomButton("a", "A", "E734", "a/b", null, false), adding: true));
        Assert.Equal(CustomProblem.None, ToolbarEditorView.Check(m, new CustomButton("a", "A", "E734", "fix", null, false), adding: true));
    }

    [Fact]
    public void Check_refuses_a_25th_button_but_not_editing_one()
    {
        var m = ToolbarModel.Defaults;
        for (var i = 0; i < ToolbarModel.MaxButtons; i++) m = ToolbarEditor.AddCustom(m, Text($"b{i}"));
        Assert.Equal(CustomProblem.TooManyButtons, ToolbarEditorView.Check(m, Text("more"), adding: true));
        Assert.Same(m, ToolbarEditor.AddCustom(m, Text("more")));
        Assert.Equal(CustomProblem.None, ToolbarEditorView.Check(m, Text("b3", "New label"), adding: false));
    }

    [Fact]
    public void A_button_that_passes_Check_is_always_added()
    {
        var random = new Random(5);
        string Pick(params string[] s) => s[random.Next(s.Length)];
        for (var i = 0; i < 500; i++)
        {
            var label = Pick("Summarise", "", " x ", "日本", "Settings", new string('l', 60));
            var b = random.Next(2) == 0
                ? new CustomButton("", label, Pick(ToolbarEditorView.Icons.ToArray()), Pick("fix", "a b", "x-1", ""), null, false)
                : new CustomButton("", label, Pick(ToolbarEditorView.Icons.ToArray()), null, Pick("hi", " ", "", new string('t', 3000)), random.Next(2) == 0);
            var m = ToolbarModel.Defaults;
            b = b with { Id = ToolbarEditorView.NewCustomId(m, label) };
            if (ToolbarEditorView.Check(m, b, adding: true) == CustomProblem.None) Assert.NotSame(m, ToolbarEditor.AddCustom(m, b));
        }
    }

    // ---- the look choices ----

    [Fact]
    public void Every_size_preset_is_offered_in_order_with_its_own_name()
    {
        Assert.Equal(Enum.GetValues<ButtonSize>(), ToolbarEditorView.Sizes);
        var names = ToolbarEditorView.Sizes.Select(ToolbarEditorView.SizeName).ToList();
        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Outline_colors_are_eight_valid_distinct_swatches_with_names_and_survive_a_save()
    {
        Assert.Equal(8, ToolbarEditorView.OutlineColors.Count);
        Assert.Equal(8, ToolbarEditorView.OutlineColors.Distinct().Count());
        var names = ToolbarEditorView.OutlineColors.Select(ToolbarEditorView.ColorName).Append(ToolbarEditorView.ColorName(null)).ToList();
        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(9, names.Distinct().Count());
        foreach (var hex in ToolbarEditorView.OutlineColors)
        {
            Assert.Matches("^#[0-9A-F]{6}$", hex);
            var m = ToolbarEditor.SetStyle(ToolbarModel.Defaults, null, ToolbarModel.Defaults.Style with { Outline = true, OutlineColor = hex });
            Assert.Equal(hex, m.Style.OutlineColor);
        }
        Assert.Equal("#123456", ToolbarEditorView.ColorName("#123456")); // a colour set in the file by hand: shown as it is
    }

    [Fact]
    public void Three_sizes_are_offered_smallest_first()
    {
        Assert.Equal([ButtonSize.Compact, ButtonSize.Normal, ButtonSize.Wide], ToolbarEditorView.Sizes);
        Assert.Equal(BarSizeChoice.Wide, (BarSizeChoice)ButtonSize.Wide); // the choice shares the sizes' values
    }

    [Theory]
    [InlineData("", "", BarSizeChoice.Normal)] // no size anywhere: normal
    [InlineData("\"size\": \"wide\"", "", BarSizeChoice.Wide)]
    [InlineData("\"size\": \"compact\"", "\"pin\": { \"outline\": true }", BarSizeChoice.Compact)] // other keys don't matter
    [InlineData("\"size\": \"wide\"", "\"pin\": { \"size\": \"wide\" }", BarSizeChoice.Wide)] // the same size again
    [InlineData("\"size\": \"wide\"", "\"pin\": { \"size\": \"compact\" }", BarSizeChoice.Custom)]
    [InlineData("", "\"settings\": { \"size\": \"wide\", \"filled\": true }", BarSizeChoice.Custom)]
    public void The_bar_size_shows_custom_only_while_an_item_has_another_size(string style, string overrides, BarSizeChoice expected)
    {
        var m = ToolbarModel.Resolve($$"""
            { "version": 2, "items": { "left": ["pin", "settings"], "right": [] }, "style": { {{style}} }, "overrides": { {{overrides}} } }
            """);
        Assert.Empty(m.Warnings);
        Assert.Equal(expected, ToolbarEditorView.BarSize(m));
        // Choosing a size for the whole bar always leaves it showing that size.
        foreach (var size in ToolbarEditorView.Sizes) Assert.Equal((BarSizeChoice)size, ToolbarEditorView.BarSize(ToolbarEditor.SetBarSize(m, size)));
    }
}
