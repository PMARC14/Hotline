using Hotline.Core.Platform;

namespace Hotline.Core.Tests;

/// <summary>Features and programs that aren't on every PC: found up front, explained clearly when missing.</summary>
public sealed class PlatformTests
{
    private static Func<string, bool> Files(params string[] paths) =>
        p => paths.Contains(p, StringComparer.OrdinalIgnoreCase);

    private const string Path = @"C:\Windows\System32;C:\Program Files\nodejs;C:\Users\me\.local\bin";
    private const string Ext = ".COM;.EXE;.BAT;.CMD";

    [Fact]
    public void A_bare_command_is_found_on_path_with_its_extension() =>
        Assert.Equal(@"C:\Program Files\nodejs\npx.cmd", CommandResolver.Find("npx", null, Path, Ext, Files(@"C:\Program Files\nodejs\npx.cmd")));

    [Fact]
    public void A_command_with_an_extension_or_a_full_path_is_checked_as_given()
    {
        Assert.Equal(@"C:\Users\me\.local\bin\uvx.exe", CommandResolver.Find("uvx.exe", null, Path, Ext, Files(@"C:\Users\me\.local\bin\uvx.exe")));
        Assert.Equal(@"D:\tools\server.exe", CommandResolver.Find(@"D:\tools\server.exe", null, Path, Ext, Files(@"D:\tools\server.exe")));
        Assert.Null(CommandResolver.Find(@"D:\tools\missing.exe", null, Path, Ext, Files()));
    }

    [Fact]
    public void A_relative_command_is_looked_up_in_the_working_directory() =>
        Assert.Equal(@"C:\work\run.cmd", CommandResolver.Find(@".\run", @"C:\work", Path, Ext, Files(@"C:\work\run.cmd")));

    [Fact]
    public void A_missing_command_is_reported_as_missing() => Assert.Null(CommandResolver.Find("npx", null, Path, Ext, Files()));

    [Fact]
    public void CommandResolver_survives_a_command_with_a_nul_character()
        => Assert.Null(CommandResolver.Find("np\0x", @"C:\work", @"C:\bin", ".EXE", _ => false));

    [Fact]
    public void CommandResolver_never_touches_a_network_path()
    {
        var touched = new List<string>();
        bool Exists(string p) { touched.Add(p); return false; }
        Assert.Null(CommandResolver.Find(@"\\evil\share\tool.exe", null, "", ".EXE", Exists));
        Assert.Null(CommandResolver.Find(@"\\evil\share\tool", null, "", ".EXE", Exists));
        Assert.Null(CommandResolver.Find("tool", @"\\evil\share", "", ".EXE", Exists));
        Assert.Null(CommandResolver.Find(".\\tool", @"\\evil\share", "", ".EXE", Exists));
        Assert.Null(CommandResolver.Find("tool", null, @"\\evil\share", ".EXE", Exists));
        Assert.Empty(touched);
    }

    [Theory]
    [InlineData("npx", "Node.js")]
    [InlineData("node", "Node.js")]
    [InlineData("uvx", "uv")]
    [InlineData("python", "Python")]
    [InlineData("docker", "Docker Desktop")]
    [InlineData("mytool", "isn't installed or isn't on PATH")] // unknown: still a clear message
    public void The_message_says_what_to_install(string command, string install)
    {
        var message = CommandResolver.MissingMessage("files", command);
        Assert.Contains($"\"{command}\"", message);
        Assert.Contains(install, message);
    }

    [Theory]
    [InlineData(22631, 0, false)]
    [InlineData(26100, 1, true)]
    [InlineData(26200, 9457, true)]
    public void App_actions_need_24h2(int build, int revision, bool expected) =>
        Assert.Equal(expected, PlatformSupport.AppActions(new WindowsBuild(build, revision)));

    [Theory]
    [InlineData(26200, 9457, false)]
    [InlineData(26220, 7261, false)]
    [InlineData(26220, 7262, true)]
    [InlineData(26300, 9550, true)]
    public void Windows_agent_connectors_need_26220_7262(int build, int revision, bool expected) =>
        Assert.Equal(expected, PlatformSupport.AgentConnectors(new WindowsBuild(build, revision)));

    [Theory]
    [InlineData(26300, 9711, false, AgentConnectorsState.PreviewNotDelivered)] // 26H2 without odr.exe: the owner's PC
    [InlineData(26300, 9711, true, AgentConnectorsState.Ready)]
    [InlineData(26220, 7262, false, AgentConnectorsState.PreviewNotDelivered)]
    [InlineData(26100, 1, false, AgentConnectorsState.BuildTooOld)]
    [InlineData(26200, 9457, false, AgentConnectorsState.BuildTooOld)]
    [InlineData(26100, 1, true, AgentConnectorsState.Ready)] // found is found, whatever the build number says
    public void Agent_connectors_state_says_why_they_are_not_there(int build, int revision, bool odrPresent, AgentConnectorsState expected) =>
        Assert.Equal(expected, PlatformSupport.AgentConnectorsStateOf(new WindowsBuild(build, revision), odrPresent));
}
