using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>The pure half of Settings' quadrant editor: drops, the strips' units and arrows,
/// custom button ids, the icon list and the checks behind its notices.</summary>
public sealed class ToolbarEditorViewTests
{
    private const Quadrant BL = Quadrant.BottomLeft, BR = Quadrant.BottomRight, TL = Quadrant.TopLeft, TR = Quadrant.TopRight;

    private static ToolbarModel Model(string bottomLeft, string topLeft, string topRight = "", string bottomRight = "", string buttons = "") =>
        ToolbarModel.Resolve($$"""
            { "version": 2, "items": { "left": [{{bottomLeft}}], "right": [{{bottomRight}}] }, "top": { "left": [{{topLeft}}], "right": [{{topRight}}] },
              "buttons": { {{buttons}} } }
            """);

    /// <summary>top left: continueIn, effort, model, provider, newChat (the pickers at 1-3).</summary>
    private static readonly ToolbarModel Block = Model("\"pin\"", "\"continueIn\", \"effort\", \"model\", \"provider\", \"newChat\"", "\"recent\"");

    private static CustomButton Text(string id, string label = "Label") => new(id, label, "E8A5", null, "hello", false);

    // ---- Drop ----

    [Fact]
    public void Drop_places_an_item_where_it_is_dropped_pickers_and_hidden_items_too()
    {
        // The pickers are no block any more: a gap between two of them is a gap like any other.
        Assert.Equal(["continueIn", "effort", "model", "pin", "provider", "newChat"], ToolbarEditor.Drop(Block, "pin", TL, 3).Items(TL));
        Assert.Empty(ToolbarEditor.Drop(Block, "pin", TL, 3).Items(BL));
        // A picker moves on its own.
        var picker = ToolbarEditor.Drop(Block, "model", BL, 0);
        Assert.Equal(["model", "pin"], picker.Items(BL));
        Assert.Equal(["continueIn", "effort", "provider", "newChat"], picker.Items(TL));
        // A hidden item is shown there.
        var shown = ToolbarEditor.Drop(Block, "settings", TR, 0);
        Assert.Equal(["settings", "recent"], shown.Items(TR));
        Assert.DoesNotContain("settings", ToolbarEditor.Hidden(shown));
    }

    [Fact]
    public void Drop_onto_its_own_place_or_of_an_unknown_id_changes_nothing()
    {
        Assert.Same(Block, ToolbarEditor.Drop(Block, "continueIn", TL, 0));
        Assert.Same(Block, ToolbarEditor.Drop(Block, "continueIn", TL, 1));
        Assert.Same(Block, ToolbarEditor.Drop(Block, "model", TL, 2)); // a picker onto its own place
        Assert.Same(Block, ToolbarEditor.Drop(Block, "model", TL, 3));
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
    public void Units_show_each_item_alone_pickers_too_in_file_order()
    {
        var m = Model("\"pin\"", "\"continueIn\", \"provider\", \"effort\", \"newChat\"", buttons: "");
        var units = ToolbarEditorView.Units(m, TL);
        Assert.Equal(["continueIn", "provider", "effort", "newChat"], units.Select(u => u.Id));
        Assert.Equal([false, true, true, false], units.Select(u => u.IsPicker));
        Assert.Empty(ToolbarEditorView.Units(m, TR));
    }

    [Fact]
    public void Units_arrows_are_off_at_the_edges()
    {
        var units = ToolbarEditorView.Units(Block, TL);
        Assert.False(units[0].CanMoveLeft);
        Assert.True(units[0].CanMoveRight);
        Assert.True(units[1].CanMoveLeft && units[1].CanMoveRight);
        Assert.True(units[3].CanMoveLeft && units[3].CanMoveRight); // a picker in the middle moves both ways
        Assert.False(units[4].CanMoveRight);
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

    // ---- Show (the Not shown strip's add button) ----

    [Fact]
    public void Show_puts_an_item_or_a_hidden_picker_at_the_end_of_bottom_left()
    {
        var m = ToolbarEditorView.Show(Block, "settings");
        Assert.Equal(["pin", "settings"], m.Items(BL));
        Assert.Same(Block, ToolbarEditorView.Show(Block, "pin")); // already shown
        Assert.Same(Block, ToolbarEditorView.Show(Block, "nope"));
        // A hidden picker goes there like a button; the other pickers stay where they are.
        var picker = ToolbarEditorView.Show(ToolbarEditor.Hide(Block, "effort"), "effort");
        Assert.Equal(["pin", "effort"], picker.Items(BL));
        Assert.Equal(["continueIn", "model", "provider", "newChat"], picker.Items(TL));
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
        // Cut at 32 it would end in "-": "aaa...a-b" loses the dash too.
        Assert.Equal(new string('a', 31), ToolbarEditorView.NewCustomId(ToolbarModel.Defaults, new string('a', 31) + " b"));
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
        Assert.Equal([ButtonSize.Compact, ButtonSize.Normal, ButtonSize.Wide], ToolbarEditorView.Sizes); // smallest first
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
