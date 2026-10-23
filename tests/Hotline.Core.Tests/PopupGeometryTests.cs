using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class PopupGeometryTests
{
    [Theory]
    [InlineData(0, 0, 1920, 1040, 640, 520, 1.0, 0.5, 640, 260, 640, 520)]       // centered at half
    [InlineData(0, 0, 3840, 1440, 640, 520, 1.5, 0.5, 1440, 330, 960, 780)]      // dpi scale
    [InlineData(-1920, 0, 1920, 1080, 640, 520, 1.0, 0.5, -1280, 280, 640, 520)] // monitor left of primary
    [InlineData(100, 50, 800, 600, 2000, 2000, 1.0, 0.8, 100, 50, 800, 600)]     // clamped when too big
    public void Places_the_popup_in_the_work_area(int wx, int wy, int ww, int wh, int w, int h, double dpi, double position,
        int x, int y, int outW, int outH)
        => Assert.Equal(new RectI(x, y, outW, outH), PopupGeometry.Place(new RectI(wx, wy, ww, wh), w, h, dpi, verticalPosition: position));

    [Theory]
    [InlineData(0.0, 0)]      // top edge of the work area
    [InlineData(1.0, 920)]    // bottom edge (1040 - 120)
    [InlineData(0.8, 736)]    // default: lower part of the screen, like Copilot's quick view
    [InlineData(-3.0, 0)]     // out of range is clamped
    [InlineData(7.0, 920)]
    public void Vertical_position_is_a_fraction_of_free_space(double position, int expectedY)
        => Assert.Equal(expectedY, PopupGeometry.Place(new RectI(0, 0, 1920, 1040), 560, 120, 1.0, position).Y);
}
