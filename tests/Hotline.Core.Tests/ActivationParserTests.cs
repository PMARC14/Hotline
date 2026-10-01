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
    public void ParseUri_maps_known_states(string uri, KeyEvent expected)
        => Assert.Equal(expected, ActivationParser.ParseUri(new Uri(uri)));

    [Theory]
    [InlineData("hotline://key")]
    [InlineData("hotline://key?state=Wiggle")]
    [InlineData("hotline://key?state=")]
    [InlineData("https://key?state=Tap")]
    public void ParseUri_returns_null_for_unknown_input(string uri)
        => Assert.Null(ActivationParser.ParseUri(new Uri(uri)));

    [Fact]
    public void ParseUri_returns_null_for_null()
        => Assert.Null(ActivationParser.ParseUri(null));

    [Theory]
    [InlineData(0u, KeyEvent.Tap)]
    [InlineData(1u, KeyEvent.HoldStart)]
    [InlineData(2u, KeyEvent.HoldStop)]
    public void ParseFastPath_maps_manifest_wparams(uint wParam, KeyEvent expected)
        => Assert.Equal(expected, ActivationParser.ParseFastPath(wParam));

    [Fact]
    public void ParseFastPath_returns_null_for_unknown_wparam()
        => Assert.Null(ActivationParser.ParseFastPath(7));
}
