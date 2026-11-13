using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class EmbeddedWidthsTests
{
    [Fact]
    public void Narrow_indented_viewport_never_overflows()
    {
        var width = EmbeddedWidths.ForViewport(150, 2);
        Assert.Equal(106, width);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    [InlineData(-50, 0)]
    [InlineData(-50, 2)]
    [InlineData(10, 1)]
    public void Zero_and_negative_viewport_cases_never_return_negative_width(double viewport, int indent)
    {
        var width = EmbeddedWidths.ForViewport(viewport, indent);
        Assert.Equal(0, width);
    }
}
