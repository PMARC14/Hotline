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

    // Bar (empty panel) 120 px, maximum 720 px, so halfway is 420 px.
    [Theory]
    [InlineData(120, 126, 126)] // empty panel: the bar measures a little taller than the minimum — fits exactly
    [InlineData(120, 200, 420)] // a message was sent: straight to halfway
    [InlineData(420, 400, 420)] // the answer streams in below halfway: no resize
    [InlineData(420, 500, 720)] // it outgrows halfway: straight to the maximum
    [InlineData(720, 300, 720)] // shorter mid-chat (an edit, a notice gone): hold
    [InlineData(720, 140, 140)] // new chat (back to just the bar): fits again
    [InlineData(120, 900, 720)] // a long chat reopened: the maximum at once
    [InlineData(800, 500, 720)] // never above the maximum (the screen or setting changed)
    public void Panel_grows_in_two_sizes_halfway_then_full(int previous, int content, int expected)
        => Assert.Equal(expected, PopupGeometry.TwoStep(previous, content, floorPx: 120, maxPx: 720, slackPx: 48));

    [Theory]
    [InlineData(4000, 3000, 2048, 2048, 1536)]
    [InlineData(1000, 3000, 2048, 683, 2048)]
    [InlineData(800, 600, 2048, 800, 600)]   // never upscale
    [InlineData(5000, 1, 100, 100, 1)]       // never zero
    public void Fit_within_keeps_aspect(int w, int h, int max, int ew, int eh)
        => Assert.Equal((ew, eh), ImageMath.FitWithin(w, h, max));
}
