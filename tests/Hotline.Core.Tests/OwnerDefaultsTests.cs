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
        Assert.Equal(new ButtonStyle(ButtonSize.Normal, false, false), d.Style);
        Assert.Empty(d.Overrides);
        Assert.Equal((3, 4, 4), (d.EffectiveSpan("effort"), d.EffectiveSpan("model"), d.EffectiveSpan("provider")));
    }

    [Fact]
    public void Default_rows_survive_a_write_and_read()
        => Assert.Equal(ToolbarModel.Defaults, ToolbarModel.Resolve(ToolbarModel.Defaults.ToJson()));

    [Fact]
    public void Default_window_sizes_are_the_owners()
    {
        var w = new HotlineSettings().Window;
        Assert.Equal((25.0, 60.0, 1000), (w.WidthPercent, w.MaxHeightPercent, w.MaxWidth));
        Assert.Equal((0.5, 0.8), (w.Anchor.X, w.Anchor.Y));
    }
}
