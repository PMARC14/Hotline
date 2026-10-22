using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>The pickers' widths as whole numbers of button widths ("span"): the file, the editor and the layout.</summary>
public sealed class ToolbarPickerSpanTests
{
    private static ToolbarModel Read(string overrides, string pickers = "\"effort\", \"model\", \"provider\"") => ToolbarModel.Resolve(
        $$"""{ "version": 2, "items": { "left": ["pin", {{pickers}}] }, "overrides": {{overrides}} }""");

    // ---- the file ----

    [Fact]
    public void Defaults_are_two_four_and_three_button_widths()
    {
        var m = ToolbarModel.Defaults;
        Assert.Equal((2, 4, 3), (m.EffectiveSpan("effort"), m.EffectiveSpan("model"), m.EffectiveSpan("provider")));
        Assert.Empty(m.PickerSpans);
        Assert.Equal((1, 8), (ToolbarModel.MinSpan, ToolbarModel.MaxSpan));
    }

    [Fact]
    public void A_span_override_is_read_and_is_not_a_warning()
    {
        var m = Read("""{ "model": { "span": 6 }, "EFFORT": { "span": 1 } }""");
        Assert.Equal(6, m.EffectiveSpan("model"));
        Assert.Equal(1, m.EffectiveSpan("effort"));
        Assert.Equal(3, m.EffectiveSpan("provider"));
        Assert.Empty(m.Warnings);
        Assert.Empty(m.Overrides); // spans are not a ButtonStyle
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("-3")]
    [InlineData("2.5")]
    [InlineData("\"4\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[3]")]
    [InlineData("1e400")]
    public void A_bad_span_is_dropped_with_a_warning(string value)
    {
        var m = Read($$"""{ "model": { "span": {{value}} } }""");
        Assert.Equal(4, m.EffectiveSpan("model"));
        Assert.Empty(m.PickerSpans);
        Assert.Contains(m.Warnings, w => w.Contains("span"));
    }

    [Fact]
    public void A_whole_number_written_with_a_fraction_part_of_zero_is_a_span()
        => Assert.Equal(5, Read("""{ "model": { "span": 5.0 } }""").EffectiveSpan("model"));

    [Fact]
    public void Other_keys_on_a_picker_are_still_ignored_with_a_warning_but_the_span_stays()
    {
        var m = Read("""{ "model": { "outline": true, "span": 7 } }""");
        Assert.Equal(7, m.EffectiveSpan("model"));
        Assert.Empty(m.Overrides);
        Assert.Contains(m.Warnings, w => w.Contains("model"));
    }

    [Fact]
    public void A_picker_override_that_is_not_an_object_is_ignored_with_a_warning()
    {
        var m = Read("""{ "model": 6 }""");
        Assert.Equal(4, m.EffectiveSpan("model"));
        Assert.Contains(m.Warnings, w => w.Contains("model"));
    }

    [Fact]
    public void A_second_override_for_a_picker_is_ignored()
    {
        var m = Read("""{ "model": { "span": 6 }, "MODEL": { "span": 2 } }""");
        Assert.Equal(6, m.EffectiveSpan("model"));
        Assert.Contains(m.Warnings, w => w.Contains("Second"));
    }

    [Fact]
    public void A_span_equal_to_the_default_is_not_stored()
    {
        var m = Read("""{ "model": { "span": 4 } }""");
        Assert.Empty(m.PickerSpans);
        Assert.Empty(m.Warnings);
        Assert.Equal(Read("{}"), m);
    }

    [Fact]
    public void A_hidden_pickers_span_is_kept()
    {
        var m = ToolbarModel.Resolve("""{ "version": 2, "items": { "left": ["pin"] }, "overrides": { "model": { "span": 6 } } }""");
        Assert.Equal(6, m.EffectiveSpan("model"));
        Assert.Empty(m.Warnings);
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
    }

    [Fact]
    public void Spans_round_trip_through_the_writer_next_to_style_overrides()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 2, "items": { "left": ["pin", "effort", "model", "provider"] },
              "overrides": { "pin": { "outline": true }, "provider": { "span": 8 }, "effort": { "span": 1 } } }
            """);
        var text = m.ToJson();
        Assert.Contains("\"provider\": { \"span\": 8 }", text);
        var again = ToolbarModel.Resolve(text);
        Assert.Equal(m, again);
        Assert.Empty(again.Warnings);
        Assert.Equal(new ButtonStyle(Outline: true), again.Overrides["pin"]);
        Assert.Equal((1, 8), (again.EffectiveSpan("effort"), again.EffectiveSpan("provider")));
    }

    [Fact]
    public void Models_with_different_spans_are_not_equal()
    {
        var a = Read("""{ "model": { "span": 6 } }""");
        var b = Read("""{ "model": { "span": 5 } }""");
        Assert.NotEqual(a, b);
        Assert.Equal(a, Read("""{ "MODEL": { "span": 6 } }"""));
        Assert.Equal(a.GetHashCode(), Read("""{ "MODEL": { "span": 6 } }""").GetHashCode());
    }

    [Fact]
    public void EffectiveSpan_of_anything_else_is_one_and_clamps_an_in_memory_value()
    {
        var m = ToolbarModel.Defaults with { PickerSpans = new Dictionary<string, int> { ["model"] = 99, ["effort"] = -4 } };
        Assert.Equal(8, m.EffectiveSpan("model"));
        Assert.Equal(1, m.EffectiveSpan("effort"));
        Assert.Equal(1, m.EffectiveSpan("pin"));
        Assert.Equal(1, m.EffectiveSpan(null!));
    }

    // ---- the editor ----

    [Fact]
    public void SetPickerSpan_sets_clamps_and_round_trips()
    {
        var m = ToolbarModel.Defaults;
        var six = ToolbarEditor.SetPickerSpan(m, "model", 6);
        Assert.Equal(6, six.EffectiveSpan("model"));
        Assert.Equal(six, ToolbarModel.Resolve(six.ToJson()));
        Assert.Equal(8, ToolbarEditor.SetPickerSpan(m, "model", 99).EffectiveSpan("model"));
        Assert.Equal(1, ToolbarEditor.SetPickerSpan(m, "MODEL", -5).EffectiveSpan("model"));
    }

    [Fact]
    public void SetPickerSpan_back_to_the_default_removes_the_entry()
    {
        var six = ToolbarEditor.SetPickerSpan(ToolbarModel.Defaults, "model", 6);
        var back = ToolbarEditor.SetPickerSpan(six, "model", 4);
        Assert.Empty(back.PickerSpans);
        Assert.Equal(ToolbarModel.Defaults, back);
    }

    [Fact]
    public void SetPickerSpan_that_changes_nothing_returns_the_input()
    {
        var m = ToolbarEditor.SetPickerSpan(ToolbarModel.Defaults, "model", 6);
        Assert.Same(m, ToolbarEditor.SetPickerSpan(m, "model", 6));
    }

    [Fact]
    public void SetPickerSpan_same_instance_cases()
    {
        var m = ToolbarModel.Defaults;
        Assert.Same(m, ToolbarEditor.SetPickerSpan(m, "model", 4)); // the default already
        Assert.Same(m, ToolbarEditor.SetPickerSpan(m, "pin", 3)); // not a picker
        Assert.Same(m, ToolbarEditor.SetPickerSpan(m, "nope", 3));
        Assert.Same(m, ToolbarEditor.SetPickerSpan(m, null!, 3));
        Assert.Null(ToolbarEditor.SetPickerSpan(null!, "model", 3));
        var eight = ToolbarEditor.SetPickerSpan(m, "model", 8);
        Assert.Same(eight, ToolbarEditor.SetPickerSpan(eight, "model", 200)); // clamps to the 8 it has
    }

    [Fact]
    public void SetPickerSpan_works_on_a_hidden_picker_and_hide_keeps_the_span()
    {
        var hidden = ToolbarEditor.Hide(ToolbarModel.Defaults, "model");
        var set = ToolbarEditor.SetPickerSpan(hidden, "model", 7);
        Assert.Equal(7, set.EffectiveSpan("model"));
        Assert.Equal(set, ToolbarModel.Resolve(set.ToJson()));
        var shownAgain = ToolbarEditor.Show(set, "model", Quadrant.BottomRight);
        Assert.Equal(7, shownAgain.EffectiveSpan("model"));
        Assert.Equal(7, ToolbarEditor.Hide(shownAgain, "model").EffectiveSpan("model"));
    }

    [Fact]
    public void Moving_the_pickers_keeps_their_spans()
    {
        var m = ToolbarEditor.SetPickerSpan(ToolbarModel.Defaults, "provider", 5);
        var moved = ToolbarEditor.ShiftTo(m, "model", Shift.Down);
        Assert.Equal(5, moved.EffectiveSpan("provider"));
        Assert.Contains("model", moved.Items(Quadrant.BottomLeft));
    }

    // ---- the layout ----

    [Theory]
    [InlineData(ButtonSize.Compact)]
    [InlineData(ButtonSize.Normal)]
    [InlineData(ButtonSize.Wide)]
    public void The_unit_is_one_standard_button_width_whatever_the_bar_size(ButtonSize size)
    {
        Assert.Equal(ToolbarPlan.BaseWidth, ToolbarPlan.PickerUnit);
        var specs = ToolbarPlan.PickerSpecs(ToolbarModel.Defaults with { Style = new ButtonStyle(size) }, ["effort", "model", "provider"]).ToDictionary(s => s.Name);
        Assert.Equal((80.0, 160.0, 120.0), (specs["effort"].Preferred, specs["model"].Preferred, specs["provider"].Preferred));
    }

    [Fact]
    public void The_unit_ignores_one_buttons_own_size()
    {
        var m = ToolbarModel.Defaults with { Overrides = new Dictionary<string, ButtonStyle> { ["pin"] = new(Width: 80) } };
        Assert.Equal(80.0, ToolbarPlan.PickerSpecs(m, ["effort"]).First(s => s.Name == "effort").Preferred);
    }

    [Fact]
    public void Specs_are_span_times_unit_with_a_readable_floor_capped_at_the_pickers_own_width()
    {
        var m = ToolbarModel.Defaults;
        var specs = ToolbarPlan.PickerSpecs(m, ["effort", "model", "provider"]).ToDictionary(s => s.Name);
        Assert.Equal((80.0, 160.0, 120.0), (specs["effort"].Preferred, specs["model"].Preferred, specs["provider"].Preferred));
        Assert.Equal((56.0, 84.0, 72.0), (specs["effort"].Min, specs["model"].Min, specs["provider"].Min));
        Assert.All(specs.Values, s => Assert.Equal(s.Preferred, s.Max));
        // A compact bar gets the same: the unit does not follow the bar's size.
        var compact = ToolbarPlan.PickerSpecs(m with { Style = new ButtonStyle(ButtonSize.Compact) }, ["effort", "model", "provider"]);
        Assert.Equal(specs.Values.Select(s => (s.Name, s.Preferred, s.Min)), compact.Select(s => (s.Name, s.Preferred, s.Min)));
        // Span 1 is 40 DIP exactly, as the preferred width, the minimum and the maximum.
        foreach (var id in new[] { "effort", "model", "provider" })
        {
            var one = ToolbarPlan.PickerSpecs(ToolbarEditor.SetPickerSpan(m, id, 1), [id]).First(s => s.Name == id);
            Assert.Equal((40.0, 40.0, 40.0), (one.Preferred, one.Min, one.Max ?? -1));
        }
        // Span 2 of the provider is 80, over its floor of 72: the floor stays 72.
        var two = ToolbarPlan.PickerSpecs(ToolbarEditor.SetPickerSpan(m, "provider", 2), ["provider"]).First(s => s.Name == "provider");
        Assert.Equal((80.0, 72.0), (two.Preferred, two.Min));
    }

    [Fact]
    public void An_unavailable_picker_is_not_available_in_the_specs()
    {
        var specs = ToolbarPlan.PickerSpecs(ToolbarModel.Defaults, ["model"]).ToDictionary(s => s.Name);
        Assert.False(specs["effort"].Available);
        Assert.True(specs["model"].Available);
        Assert.False(specs["provider"].Available);
    }

    [Fact]
    public void A_span_is_a_maximum_too()
    {
        var m = ToolbarEditor.SetPickerSpan(ToolbarModel.Defaults, "model", 8);
        var specs = ToolbarPlan.PickerSpecs(m, ["effort", "model", "provider"]);
        var widths = ToolbarLayout.Compute(5000, 100, 4, specs, ["effort", "provider", "model"]);
        Assert.Equal((80.0, 320.0, 120.0), (widths["effort"], widths["model"], widths["provider"]));
    }
}
