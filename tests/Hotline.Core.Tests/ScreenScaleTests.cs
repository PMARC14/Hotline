using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class ScreenScaleTests
{
    [Theory]
    [InlineData(2880, 1800, 2.0, 1440, 900)]
    [InlineData(2880, 1800, 1.5, 1920, 1200)]
    [InlineData(1920, 1080, 1.25, 1536, 864)]
    [InlineData(1920, 1080, 1.0, 1920, 1080)]
    [InlineData(1920, 1080, 0, 1920, 1080)] // an unknown scale counts as 100%
    public void The_interface_size_is_the_pixels_over_the_scale(int w, int h, double scale, int uiW, int uiH) =>
        Assert.Equal((uiW, uiH), ScreenScale.Interface(w, h, scale));

}
