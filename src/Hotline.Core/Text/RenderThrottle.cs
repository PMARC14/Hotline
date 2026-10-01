namespace Hotline.Core.Text;

/// <summary>Streaming re-render pacing: about 20 fps for cheap renders, backing off to 3x the last render's cost (max 1 s).</summary>
public static class RenderThrottle
{
    public static TimeSpan NextInterval(TimeSpan lastRenderCost)
        => TimeSpan.FromMilliseconds(Math.Clamp(lastRenderCost.TotalMilliseconds * 3, 50, 1000));
}
