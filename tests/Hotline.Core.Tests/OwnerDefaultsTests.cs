using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>The shipped defaults are the owner's own configuration (2026-10-22).</summary>
public sealed class OwnerDefaultsTests
{
    [Fact]
    public void Default_button_rows_are_the_owners()
    {
        var d = ToolbarModel.Defaults;
        Assert.Equal(["effort"], d.Items(Quadrant.TopLeft));
        Assert.Equal(["model"], d.Items(Quadrant.TopCenter));
        Assert.Equal(["provider"], d.Items(Quadrant.TopRight));
        Assert.Equal(["pin", "captureWindow", "captureScreen", "captureRegion"], d.Items(Quadrant.BottomLeft));
        Assert.Equal(["prompt", "continueIn"], d.Items(Quadrant.BottomCenter));
        Assert.Equal(["recent", "newChat", "settings"], d.Items(Quadrant.BottomRight));
        Assert.Equal(new ButtonStyle(1, false, false), d.Style);
        Assert.Equal(["newChat", "recent", "settings"], d.Overrides.Keys.Order()); // two cells across each
        Assert.All(d.Overrides.Values, o => Assert.Equal(new ButtonStyle(2), o));
        Assert.Equal((3, 4, 5), (d.EffectiveSpan("effort"), d.EffectiveSpan("model"), d.EffectiveSpan("provider")));
    }

    [Fact]
    public void Default_rows_survive_a_write_and_read()
        => Assert.Equal(ToolbarModel.Defaults, ToolbarModel.Resolve(ToolbarModel.Defaults.ToJson()));

    [Fact]
    public void Default_window_sizes_are_the_owners()
    {
        var w = new HotlineSettings().Window;
        Assert.Equal((40.0, 60.0, 400, 1200), (w.WidthPercent, w.MaxHeightPercent, w.MinWidth, w.MaxWidth));
        Assert.Equal((0.5, 0.8), (w.Anchor.X, w.Anchor.Y));
    }
}
