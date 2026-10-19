using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ToolbarMigrationTests
{
    private static string Arr(params object?[] items) =>
        "[" + string.Join(", ", items.Select(i => i switch { null => "null", string s => $"\"{s}\"", _ => i.ToString() })) + "]";

    private static string V1(object?[] top, object?[] items) => $"{{ \"top\": {Arr(top)}, \"items\": {Arr(items)} }}";

    private static string[] Flat(ToolbarModel m, Quadrant left, Quadrant right) => [.. m.Items(left), .. m.Items(right)];

    [Fact]
    public void No_spacer_everything_goes_left()
    {
        var m = ToolbarMigration.FromV1(V1([], ["pin", "prompt", "settings"]));
        Assert.Equal(["pin", "prompt", "settings"], m.Items(Quadrant.BottomLeft));
        Assert.Empty(m.Items(Quadrant.BottomRight));
        Assert.Empty(m.Items(Quadrant.TopLeft));
        Assert.Empty(m.Warnings);
    }

    [Fact]
    public void Spacer_in_the_middle_splits_left_and_right()
    {
        var m = ToolbarMigration.FromV1(V1([], ["pin", "prompt", "spacer", "newChat", "settings"]));
        Assert.Equal(["pin", "prompt"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["newChat", "settings"], m.Items(Quadrant.BottomRight));
    }

    [Fact]
    public void Spacer_first_leaves_left_empty()
    {
        var m = ToolbarMigration.FromV1(V1([], ["spacer", "pin", "settings"]));
        Assert.Empty(m.Items(Quadrant.BottomLeft));
        Assert.Equal(["pin", "settings"], m.Items(Quadrant.BottomRight));
    }

    [Fact]
    public void Spacer_last_leaves_right_empty()
    {
        var m = ToolbarMigration.FromV1(V1([], ["pin", "settings", "spacer"]));
        Assert.Equal(["pin", "settings"], m.Items(Quadrant.BottomLeft));
        Assert.Empty(m.Items(Quadrant.BottomRight));
    }

    [Fact]
    public void Further_spacers_are_dropped()
    {
        var m = ToolbarMigration.FromV1(V1([], ["pin", "spacer", "prompt", "SPACER", "settings", "spacer"]));
        Assert.Equal(["pin"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["prompt", "settings"], m.Items(Quadrant.BottomRight));
    }

    [Fact]
    public void Top_row_goes_to_the_top_quadrants()
    {
        var m = ToolbarMigration.FromV1(V1(["pin", "spacer", "settings"], ["prompt"]));
        Assert.Equal(["pin"], m.Items(Quadrant.TopLeft));
        Assert.Equal(["settings"], m.Items(Quadrant.TopRight));
        Assert.Equal(["prompt"], m.Items(Quadrant.BottomLeft));
    }

    [Fact]
    public void Pickers_in_one_row_split_by_a_spacer_stay_one_block_in_order()
    {
        var m = ToolbarMigration.FromV1(V1(["effort", "spacer", "model", "provider"], ["pin"]));
        Assert.Equal(["effort", "model", "provider"], m.Items(Quadrant.TopLeft));
        Assert.Empty(m.Items(Quadrant.TopRight));
    }

    [Fact]
    public void Pickers_split_across_rows_end_up_together_where_the_first_is()
    {
        var m = ToolbarMigration.FromV1(V1(["effort", "model"], ["provider", "spacer", "pin"]));
        Assert.Equal(["provider", "effort", "model"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["pin"], m.Items(Quadrant.BottomRight));
        Assert.Empty(m.Items(Quadrant.TopLeft));
    }

    [Fact]
    public void Item_in_both_rows_stays_in_the_bottom_row()
    {
        var m = ToolbarMigration.FromV1(V1(["pin", "prompt"], ["pin"]));
        Assert.Equal(["pin"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["prompt"], m.Items(Quadrant.TopLeft));
    }

    [Fact]
    public void Duplicates_are_dropped_but_spacers_count()
    {
        var m = ToolbarMigration.FromV1(V1([], ["pin", "PIN", "spacer", "pin", "prompt"]));
        Assert.Equal(["pin"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["prompt"], m.Items(Quadrant.BottomRight));
    }

    [Fact]
    public void Junk_is_dropped_and_ids_are_case_insensitive()
    {
        var m = ToolbarMigration.FromV1("""{ "items": ["PIN", 5, null, "bogus", {"a": 1}, ["x"], "Spacer", "newchat", "settings"] }""");
        Assert.Equal(["pin"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["newChat", "settings"], m.Items(Quadrant.BottomRight));
    }

    [Fact]
    public void A_spacer_after_dropped_junk_still_splits()
    {
        var m = ToolbarMigration.FromV1(V1([], ["bogus", "spacer", "pin"]));
        Assert.Empty(m.Items(Quadrant.BottomLeft));
        Assert.Equal(["pin"], m.Items(Quadrant.BottomRight));
    }

    [Theory]
    [InlineData("""{ "top": [], "items": [] }""")]
    [InlineData("""{ "items": [] }""")]
    [InlineData("""{ "top": ["bogus", 1], "items": ["spacer", "spacer", null] }""")]
    [InlineData("""{ "top": "pin", "items": 5 }""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Empty_or_all_invalid_gives_the_defaults(string json) =>
        Assert.Equal(ToolbarModel.Defaults, ToolbarMigration.FromV1(json));

    [Fact]
    public void Reads_a_commented_file_with_bom_trailing_commas_and_repeated_keys_like_it_loads_today()
    {
        var json = "﻿{\n  // the rows\n  \"top\": [],\n  \"items\": [\"pin\", \"settings\",],\n  \"items\": [\"prompt\", \"spacer\", \"newChat\",]\n}";
        var m = ToolbarMigration.FromV1(json);
        Assert.Equal(["prompt"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["newChat"], m.Items(Quadrant.BottomRight));
    }

    [Fact]
    public void Migrated_model_roundtrips_through_the_v2_file()
    {
        var m = ToolbarMigration.FromV1(V1(["effort", "model", "provider"], ["pin", "spacer", "settings"]));
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
    }

    public static TheoryData<string[], string[]> Orders => new()
    {
        { [], ["pin", "prompt", "settings"] },
        { [], ["pin", "spacer", "prompt", "settings"] },
        { [], ["spacer", "pin", "prompt"] },
        { [], ["pin", "prompt", "spacer"] },
        { [], ["pin", "spacer", "prompt", "spacer", "settings", "spacer", "newChat"] },
        { ["effort", "model", "provider", "spacer", "pin"], ["recent", "spacer", "settings"] },
        { ["pin", "spacer", "recent"], ["prompt", "newChat", "spacer", "settings", "continueIn"] },
        { ["captureWindow", "captureScreen", "spacer", "captureRegion"], [] },
    };

    [Theory]
    [MemberData(nameof(Orders))]
    public void Left_to_right_order_equals_v1_minus_spacers(string[] top, string[] items)
    {
        var m = ToolbarMigration.FromV1(V1([.. top], [.. items]));
        Assert.Equal(items.Where(i => i != "spacer"), Flat(m, Quadrant.BottomLeft, Quadrant.BottomRight));
        Assert.Equal(top.Where(i => i != "spacer"), Flat(m, Quadrant.TopLeft, Quadrant.TopRight));
    }
}
