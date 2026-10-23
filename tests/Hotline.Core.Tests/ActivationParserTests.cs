using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class ActivationParserTests
{
    [Theory]
    [InlineData("hotline://key?state=Tap", KeyEvent.Tap)]
    [InlineData("hotline://key?state=Down", KeyEvent.HoldStart)]
    [InlineData("hotline://key?state=Up", KeyEvent.HoldStop)]
    [InlineData("HOTLINE://key?STATE=tap", KeyEvent.Tap)]
    [InlineData("hotline://key?foo=1&state=Up", KeyEvent.HoldStop)]
    [InlineData("hotline://key", null)]
    [InlineData("hotline://key?state=Wiggle", null)]
    [InlineData("hotline://key?state=", null)]
    [InlineData("https://key?state=Tap", null)]
    public void ParseUri_maps_known_states_and_ignores_unknown_input(string uri, KeyEvent? expected)
        => Assert.Equal(expected, ActivationParser.ParseUri(new Uri(uri)));

    [Fact]
    public void ParseUri_returns_null_for_null()
        => Assert.Null(ActivationParser.ParseUri(null));

    [Theory]
    [InlineData(0u, KeyEvent.Tap)]
    [InlineData(1u, KeyEvent.HoldStart)]
    [InlineData(2u, KeyEvent.HoldStop)]
    [InlineData(7u, null)]
    public void ParseFastPath_maps_manifest_wparams_and_ignores_unknown(uint wParam, KeyEvent? expected)
        => Assert.Equal(expected, ActivationParser.ParseFastPath(wParam));
}
