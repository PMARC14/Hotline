using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>What the renderer does with a resolved toolbar.json: rows, sizes, overflow and the This PC line.</summary>
public sealed class ToolbarPlanTests : IDisposable
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
        Assert.Equal(new RowPlan(["effort"], []), ToolbarPlan.Row(m, top: true));
        Assert.Equal(new RowPlan(["pin", "captureWindow", "captureScreen", "captureRegion", "prompt", "recent", "continueIn", "newChat", "settings"], []),
            ToolbarPlan.Row(m, top: false));
        Assert.Equal(["effort", "model", "provider"], ToolbarPlan.PickerOrder(m));
    }

    [Fact]
    public void Left_and_right_groups_keep_their_order()
    {
        var m = With((Quadrant.BottomLeft, ["pin", "prompt"]), (Quadrant.BottomRight, ["newChat", "settings"]), (Quadrant.TopRight, ["recent"]));
        Assert.Equal(new RowPlan(["pin", "prompt"], ["newChat", "settings"]), ToolbarPlan.Row(m, top: false));
        Assert.Equal(new RowPlan([], ["recent"]), ToolbarPlan.Row(m, top: true));
    }

    [Fact]
    public void The_pickers_are_one_entry_at_the_first_picker_listed()
    {
        var m = With((Quadrant.BottomRight, ["newChat", "provider", "model", "settings"]));
        Assert.Equal(new RowPlan([], ["newChat", "provider", "settings"]), ToolbarPlan.Row(m, top: false));
        Assert.Equal(["provider", "model"], ToolbarPlan.PickerOrder(m));
    }

    [Fact]
    public void Pickers_split_over_places_by_hand_still_render_once_where_the_first_one_is()
    {
        // A model built in code (the self-test) can split them; the renderer must never place the block twice.
        var m = With((Quadrant.BottomLeft, ["pin", "model"]), (Quadrant.TopLeft, ["effort", "recent"]));
        Assert.Equal(new RowPlan(["pin", "model"], []), ToolbarPlan.Row(m, top: false));
        Assert.Equal(new RowPlan(["recent"], []), ToolbarPlan.Row(m, top: true));
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

    [Fact]
    public void FitRow_keeps_the_pickers_room_when_the_buttons_can_overflow_into_the_rest()
    {
        // room 300: right 36, reserved 150 (+gap), overflow 36: 1 left button of 36 fits besides the button.
        var fit = ToolbarPlan.FitRow(300, 2, [36, 36, 36, 36], [36], 150, 36);
        Assert.Equal(new RowFit(1, true, true), fit);
    }

    [Fact]
    public void FitRow_drops_the_pickers_before_letting_the_overflow_button_stick_out()
    {
        // right 36 + reserved 150 + gap = 188 fits in 200, but the overflow button (36 + gap) does not: Fit's overshoot.
        // Without the pickers' room all three left buttons fit again (the pickers then hide in ToolbarLayout.Compute).
        var fit = ToolbarPlan.FitRow(200, 2, [36, 36, 36], [36], 150, 36);
        Assert.Equal(new RowFit(3, false, false), fit);
        Assert.True(Needed(fit, 2, [36, 36, 36], [36], 150, 36) <= 200);
    }

    [Fact]
    public void FitRow_without_pickers_is_Fit()
    {
        Assert.Equal(new RowFit(1, true, false), ToolbarPlan.FitRow(100, 2, [36, 36, 36], [], 0, 36));
        Assert.Equal(new RowFit(3, false, false), ToolbarPlan.FitRow(1000, 2, [36, 36, 36], [36], 0, 36));
    }

    [Fact]
    public void FitRow_accepts_an_overshoot_that_remains_without_the_pickers()
    {
        // Even the right group alone doesn't fit: the overflow button stays so the left buttons remain reachable.
        var fit = ToolbarPlan.FitRow(40, 2, [36], [36], 100, 36);
        Assert.Equal(new RowFit(0, true, false), fit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(150)]
    [InlineData(199)]
    [InlineData(240)]
    [InlineData(330)]
    [InlineData(500)]
    public void FitRow_never_overshoots_while_the_pickers_are_kept_and_is_idempotent(double room)
    {
        double[] left = [36, 30, 46, 36], right = [36, 36];
        var fit = ToolbarPlan.FitRow(room, 2, left, right, 120, 36);
        if (fit.PickersKept) Assert.True(Needed(fit, 2, left, right, 120, 36) <= room, $"{fit} at {room}");
        Assert.Equal(fit, ToolbarPlan.FitRow(room, 2, left, right, 120, 36));
    }

    private static double Needed(RowFit fit, double spacing, double[] left, double[] right, double reserved, double overflow)
    {
        var items = fit.LeftShown + right.Length + (fit.Overflow ? 1 : 0);
        reserved = fit.PickersKept ? reserved : 0;
        var gaps = Math.Max(0, items - 1) + (reserved > 0 && items > 0 ? 1 : 0);
        return left.Take(fit.LeftShown).Sum() + right.Sum() + (fit.Overflow ? overflow : 0) + spacing * gaps + reserved;
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

    // ---- settings.json archive (carry-forward) ----

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Settings_upgrade_that_cannot_be_archived_is_reported_and_left_as_it_is()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, SettingsStore.FileName);
        const string original = """{ "schemaVersion": 13 }""";
        File.WriteAllText(path, original);
        Directory.CreateDirectory(path + ".old"); // the archive can't be written there
        var store = new SettingsStore(_dir);

        store.Load();

        Assert.True(store.ArchiveFailed);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Settings_upgrade_that_is_archived_is_not_reported()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), """{ "schemaVersion": 13 }""");
        var store = new SettingsStore(_dir);

        store.Load();

        Assert.False(store.ArchiveFailed);
    }
}
