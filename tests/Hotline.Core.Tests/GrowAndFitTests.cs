using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class GrowAndFitTests
{
    private static readonly RectI Work = new(0, 0, 1920, 1040);

    [Theory]
    [InlineData(736, 400, 560, 456, 400)]  // grows upward, keeping the bottom edge
    [InlineData(736, 50, 560, 736, 120)]   // never shrinks below the bar
    [InlineData(736, 2000, 560, 296, 560)] // capped at the maximum height
    [InlineData(100, 600, 600, 0, 600)]    // stays inside the work area's top
    public void Grows_upward_keeping_the_bottom_edge_within_the_work_area(int barY, int wanted, int max, int expectedY, int expectedHeight)
        => Assert.Equal(new RectI(680, expectedY, 560, expectedHeight), PopupGeometry.GrowUp(new RectI(680, barY, 560, 120), wanted, max, Work));

    // Bar (empty panel) 120 px, maximum 720 px, so halfway is 420 px.
    [Theory]
    [InlineData(120, 126, false, 126)] // empty panel: the bar measures a little taller than the minimum — fits exactly
    [InlineData(120, 260, false, 260)] // no message yet (a long draft, a notice): fits, no jump to halfway
    [InlineData(120, 200, true, 420)]  // the first message: straight to halfway
    [InlineData(420, 400, true, 420)]  // the answer streams in below halfway: no resize
    [InlineData(420, 500, true, 500)]  // the text outgrows halfway: only as far as it needs
    [InlineData(500, 560, true, 560)]  // and on with it
    [InlineData(720, 300, true, 720)]  // shorter mid-chat (an edit, a notice gone): hold
    [InlineData(720, 140, false, 140)] // new chat: fits the bar again
    [InlineData(120, 900, true, 720)]  // a long chat reopened: the maximum at once
    [InlineData(800, 500, true, 720)]  // never above the maximum (the screen or setting changed)
    public void Panel_expands_to_half_after_the_first_message_then_as_far_as_the_text_needs(int previous, int content, bool conversation, int expected)
        => Assert.Equal(expected, PopupGeometry.TwoStep(previous, content, floorPx: 120, maxPx: 720, conversation));

    [Theory]
    [InlineData(4000, 3000, 2048, 2048, 1536)]
    [InlineData(1000, 3000, 2048, 683, 2048)]
    [InlineData(800, 600, 2048, 800, 600)]   // never upscale
    [InlineData(5000, 1, 100, 100, 1)]       // never zero
    public void Fit_within_keeps_aspect(int w, int h, int max, int ew, int eh)
        => Assert.Equal((ew, eh), ImageMath.FitWithin(w, h, max));
}
