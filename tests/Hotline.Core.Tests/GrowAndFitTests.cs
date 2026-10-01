using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class GrowAndFitTests
{
    private static readonly RectI Work = new(0, 0, 1920, 1040);
    private static readonly RectI Bar = new(680, 736, 560, 120); // bottom edge at 856

    [Fact]
    public void Grows_upward_keeping_bottom_edge()
        => Assert.Equal(new RectI(680, 456, 560, 400), PopupGeometry.GrowUp(Bar, 400, 560, Work));

    [Fact]
    public void Never_shrinks_below_bar()
        => Assert.Equal(Bar, PopupGeometry.GrowUp(Bar, 50, 560, Work));

    [Fact]
    public void Caps_at_max_height()
        => Assert.Equal(560, PopupGeometry.GrowUp(Bar, 2000, 560, Work).Height);

    [Fact]
    public void Stays_inside_work_area_top()
    {
        var r = PopupGeometry.GrowUp(new RectI(680, 100, 560, 120), 600, 600, Work);
        Assert.Equal(0, r.Y);
        Assert.Equal(600, r.Height);
    }

    [Theory]
    [InlineData(4000, 3000, 2048, 2048, 1536)]
    [InlineData(1000, 3000, 2048, 683, 2048)]
    [InlineData(800, 600, 2048, 800, 600)]   // never upscale
    [InlineData(5000, 1, 100, 100, 1)]       // never zero
    public void Fit_within_keeps_aspect(int w, int h, int max, int ew, int eh)
        => Assert.Equal((ew, eh), ImageMath.FitWithin(w, h, max));
}
