using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

/// <summary>hotline://selftest: any web page can open the link, so options that start programs or read other apps need a marker file.</summary>
public class SelfTestOptionsTests
{
    private static Uri Link(string query) => new("hotline://selftest" + query);

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("?selection", new[] { "selection" })]
    [InlineData("?tools&voice", new[] { "tools", "voice" })]
    [InlineData("?TOOLS=1&Tools&x=y", new[] { "tools", "x" })]
    public void Options_are_read_from_the_query(string query, string[] expected) => Assert.Equal(expected, SelfTestOptions.Requested(Link(query)));

    [Fact]
    public void Without_the_marker_file_tools_and_selection_are_refused_but_voice_and_the_plain_test_run()
    {
        var (allowed, refused) = SelfTestOptions.Resolve(Link("?selection&tools&voice"), extrasAllowed: false);
        Assert.Equal(["voice"], allowed);
        Assert.Equal(["selection", "tools"], refused);
        Assert.Empty(SelfTestOptions.Resolve(Link(""), extrasAllowed: false).Refused);
        Assert.Empty(SelfTestOptions.Resolve(null, extrasAllowed: false).Refused);
    }

    [Fact]
    public void With_the_marker_file_everything_runs()
    {
        var (allowed, refused) = SelfTestOptions.Resolve(Link("?selection&tools&voice"), extrasAllowed: true);
        Assert.Equal(["selection", "tools", "voice"], allowed);
        Assert.Empty(refused);
    }

    [Fact]
    public void The_marker_lives_in_the_data_folder() =>
        Assert.Equal(Path.Combine("home", ".hotline", ".allow-selftest-extras"), SelfTestOptions.MarkerPath(Path.Combine("home", ".hotline")));
}
