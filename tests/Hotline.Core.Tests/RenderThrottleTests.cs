using Hotline.Core.Text;

namespace Hotline.Core.Tests;

public class RenderThrottleTests
{
    [Theory]
    [InlineData(5, 50)]     // cheap renders: 20 fps
    [InlineData(40, 120)]   // slow renders back off to 3x their cost
    [InlineData(400, 1000)] // capped at 1 s
    public void Interval_adapts_to_render_cost(int renderMs, int expectedMs)
        => Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), RenderThrottle.NextInterval(TimeSpan.FromMilliseconds(renderMs)));
}
