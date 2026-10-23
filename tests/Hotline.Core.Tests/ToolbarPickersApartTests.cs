using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>The effort, model and provider pickers as three separate items: each is planned where it is listed, with its own
/// span, floor and place in the hide order, in either row and any group.</summary>
public sealed class ToolbarPickersApartTests
{
    private const double Spacing = ToolbarPlan.CellGap; // the rows have no column gap: each cell holds its own padding

    private static ToolbarModel Model(string bottom, string top = "{}", string overrides = "{}") => ToolbarModel.Resolve(
        $$"""{ "version": 2, "items": {{bottom}}, "top": {{top}}, "overrides": {{overrides}} }""");

    /// <summary>effort bottom left, model bottom centre, provider bottom right, with a button in each group.</summary>
    private static readonly ToolbarModel Apart = Model("""{ "left": ["pin", "effort"], "center": ["model", "prompt"], "right": ["provider", "settings"] }""");

    [Fact]
    public void Roomy_row_gives_each_picker_its_own_span_whatever_group_it_is_in()
    {
        var f = ToolbarPlan.PlanRow(Apart, top: false, room: 900, Spacing);
        Assert.Equal(new Dictionary<string, double> { ["effort"] = 80, ["model"] = 160, ["provider"] = 120 }, f.PickerWidths);
        Assert.Equal(["pin", "effort", "model", "prompt"], f.Shown);
        Assert.Equal(["provider", "settings"], f.RightShown);
        Assert.Empty(f.Menu);
    }

    [Fact]
    public void Each_picker_keeps_its_own_span_override()
    {
        var m = Model("""{ "left": ["effort"], "center": ["model"], "right": ["provider"] }""",
            overrides: """{ "effort": { "span": 6 }, "model": { "span": 1 }, "provider": { "span": 8 } }""");
        var f = ToolbarPlan.PlanRow(m, top: false, 2000, Spacing);
        Assert.Equal(new Dictionary<string, double> { ["effort"] = 240, ["model"] = 40, ["provider"] = 320 }, f.PickerWidths);
    }

    [Fact]
    public void Each_row_plans_only_its_own_pickers()
    {
        var m = Model("""{ "left": ["pin", "model", "provider"] }""", """{ "left": ["effort"], "right": ["recent"] }""");
        Assert.Equal(["effort"], ToolbarPlan.PlanRow(m, top: true, 600, Spacing).PickerWidths.Keys);
        Assert.Equal(["model", "provider"], ToolbarPlan.PlanRow(m, top: false, 600, Spacing).PickerWidths.Keys.Order());
        // A picker with nothing to show takes no room in its row, and the other row is unaffected.
        var top = ToolbarPlan.PlanRow(m, top: true, 600, Spacing, availablePickers: ["model", "provider"]);
        Assert.Equal(0, top.PickerWidths["effort"]);
    }

    [Fact]
    public void A_tight_row_shrinks_the_separated_pickers_evenly_toward_their_own_floors()
    {
        var m = Model("""{ "left": ["effort"], "center": ["model"], "right": ["provider"] }""");
        var roomy = ToolbarPlan.PlanRow(m, top: false, 2000, Spacing);
        // Preferred 80 + 160 + 120; floors 56 + 84 + 72: a room in between shrinks all three, none past its floor or span.
        var f = ToolbarPlan.PlanRow(m, top: false, 300, Spacing);
        Assert.All(f.PickerWidths, p => Assert.InRange(p.Value, ToolbarPlan.PickerFloor(p.Key), roomy.PickerWidths[p.Key]));
        Assert.All(f.PickerWidths, p => Assert.True(p.Value < roomy.PickerWidths[p.Key], p.Key));
        Assert.True(f.Used <= 300);
        // At the sum of the floors (and the gaps) every picker is at its floor.
        var least = ToolbarPlan.PlanRow(m, top: false, f.Fit.FixedWidth + 56 + 84 + 72 + 3 * Spacing, Spacing);
        Assert.Equal(new Dictionary<string, double> { ["effort"] = 56, ["model"] = 84, ["provider"] = 72 }, least.PickerWidths);
    }

    [Fact]
    public void A_picker_never_goes_under_its_floor_it_hides_in_effort_provider_model_order()
    {
        var m = Model("""{ "left": ["model"], "center": ["provider"], "right": ["effort"] }""");
        var seen = new List<string[]>();
        for (double room = 400; room >= 60; room -= 1)
        {
            var f = ToolbarPlan.PlanRow(m, top: false, room, Spacing);
            Assert.All(f.PickerWidths, p => Assert.True(p.Value == 0 || p.Value >= ToolbarPlan.PickerFloor(p.Key) - 1e-9, $"{room} {p}"));
            Assert.True(f.Used <= room + 1e-9 || room < 100, $"{room}: {f.Used}");
            var visible = f.PickerWidths.Where(p => p.Value > 0).Select(p => p.Key).Order().ToArray();
            if (seen.Count == 0 || !seen[^1].SequenceEqual(visible)) seen.Add(visible);
        }
        // Everything, then effort goes, then provider, then model.
        Assert.Equal<string[]>([["effort", "model", "provider"], ["model", "provider"], ["model"], []], seen);
    }

    [Fact]
    public void Buttons_go_into_the_menu_once_the_pickers_are_at_their_floors_and_pickers_never_do()
    {
        var m = Model("""{ "left": ["pin", "captureWindow", "effort", "prompt"], "center": ["model", "recent"], "right": ["provider", "settings"] }""");
        var menus = 0;
        for (double room = 2000; room >= 200; room -= 1)
        {
            var f = ToolbarPlan.PlanRow(m, top: false, room, Spacing);
            if (f.Menu.Count == 0) continue;
            menus++;
            Assert.DoesNotContain(f.Menu, ToolbarPlan.IsPicker);
            if (f.Fit.PickersKept) Assert.All(f.PickerWidths, p => Assert.True(p.Value >= ToolbarPlan.PickerFloor(p.Key), $"{room}: {p}"));
            // The centre loses its last button first, then the left group's end (the existing order); the right group is the last to give up any.
            Assert.Equal(new[] { "pin", "captureWindow", "prompt", "recent" }[^f.Menu.Count..], f.Menu);
            Assert.Contains("effort", f.Shown);
            Assert.Contains("model", f.Shown);
            Assert.Contains("provider", f.RightShown);
        }
        Assert.True(menus > 20);
    }

    [Fact]
    public void The_sweep_stays_in_the_room_and_a_wider_room_never_hides_a_picker()
    {
        foreach (var m in new[] { Apart, Model("""{ "left": ["effort", "pin"], "right": ["settings", "model"] }""", """{ "center": ["provider"] }""") })
            foreach (var top in new[] { false, true })
            {
                ToolbarPlan.RowLayout? previous = null;
                for (double room = 120; room <= 1400; room += 3)
                {
                    var f = ToolbarPlan.PlanRow(m, top, room, Spacing);
                    Assert.True(f.Used <= room + 1e-9 || room < 140, $"top {top} room {room}: {f.Used}");
                    // More room never hides a picker that was shown, unless a button comes back out of the menu (it takes room first).
                    // A picker that appears may narrow the others: they share what is left.
                    if (previous is { } p && f.Menu.Count >= p.Menu.Count)
                        Assert.True(f.PickerWidths.Count(w => w.Value > 0) >= p.PickerWidths.Count(w => w.Value > 0), $"top {top} room {room}");
                    previous = f;
                }
            }
    }
}
