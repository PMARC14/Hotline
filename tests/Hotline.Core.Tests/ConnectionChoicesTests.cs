using Hotline.Core.Backends;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class ConnectionChoicesTests
{
    [Fact]
    public void The_card_always_lists_all_three_CLIs_in_a_fixed_order()
    {
        Assert.Equal([BackendType.ClaudeCode, BackendType.Codex, BackendType.Antigravity], ConnectionChoices.Plan([]).Select(c => c.Type));
        Assert.Equal(ConnectionChoices.Plan([]).Select(c => c.Type), ConnectionChoices.Plan([BackendType.Antigravity, BackendType.Codex]).Select(c => c.Type));
    }

    [Fact]
    public void Each_CLI_is_marked_installed_or_not()
    {
        var plan = ConnectionChoices.Plan([BackendType.Antigravity, BackendType.ClaudeCode]);
        Assert.Equal([(BackendType.ClaudeCode, true), (BackendType.Codex, false), (BackendType.Antigravity, true)], plan);
        Assert.All(ConnectionChoices.Plan([]), c => Assert.False(c.Installed));
        Assert.All(ConnectionChoices.Plan(ConnectionChoices.Clis), c => Assert.True(c.Installed));
    }

    [Fact]
    public void Only_CLI_connections_are_offered()
        => Assert.All(ConnectionChoices.Clis, t => Assert.True(ConnectionTypes.IsCli(t)));
}
