using Hotline.Core.Backends.Agy;
using Hotline.Core.Backends.ClaudeCode;
using Hotline.Core.Backends.Codex;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>
/// A connection's "args"/"extraArgs" (user-authored, maybe copied) never widen what a CLI may run: chat only's isolation
/// always applies, and flags that pre-approve tools are dropped in every mode (only approveAllTools loosens anything).
/// </summary>
public class CliLaunchFlagTests
{
    private static List<string> Claude(ToolMode mode, IReadOnlyList<string>? args = null, string? extra = null) =>
        ClaudeCodeProtocol.BuildArgs(new BackendProfile { Tools = mode, Args = args?.ToList(), ExtraArgs = extra }, "x").ToList();

    private static readonly string[] ClaudeLockdown = ["--tools", "", "--setting-sources", "", "--strict-mcp-config"];

    [Fact]
    public void Claude_chat_only_isolation_comes_last_even_with_custom_args()
    {
        foreach (var args in new[] { Claude(ToolMode.ChatOnly, []), Claude(ToolMode.ChatOnly, ["--verbose"]), Claude(ToolMode.ChatOnly) })
            Assert.Equal(ClaudeLockdown, args.TakeLast(ClaudeLockdown.Length));
    }

    [Theory]
    [InlineData("--tools default")]
    [InlineData("--tools=default")]
    [InlineData("--tools Bash Edit")]
    [InlineData("--setting-sources user,project")]
    [InlineData("--setting-sources=user")]
    [InlineData("--mcp-config C:\\m.json")]
    [InlineData("--mcp-config=C:\\m.json")]
    [InlineData("--agent other")]
    [InlineData("--agent=other")]
    public void Claude_chat_only_drops_the_users_isolation_flags(string flags)
    {
        foreach (var args in new[] { Claude(ToolMode.ChatOnly, flags.Split(' ')), Claude(ToolMode.ChatOnly, extra: flags) })
        {
            Assert.Equal(ClaudeLockdown, args.TakeLast(ClaudeLockdown.Length));
            Assert.Single(args, "--tools");
            Assert.Single(args, "--setting-sources");
            Assert.DoesNotContain(args, a => a is "default" or "Bash" or "Edit" or "user,project" or "other" or @"C:\m.json"
                                             || a.StartsWith("--mcp-config", StringComparison.Ordinal) || a.StartsWith("--agent", StringComparison.Ordinal)
                                             || a.Contains('='));
        }
    }

    [Theory]
    [InlineData("--allowedTools Bash Edit")]
    [InlineData("--allowedTools=Bash")]
    [InlineData("--allowed-tools Bash")]
    [InlineData("--allowed-tools=Bash")]
    [InlineData("--settings C:\\s.json")]
    [InlineData("--settings={\"permissions\":{\"defaultMode\":\"bypassPermissions\"}}")]
    [InlineData("--permission-prompt-tool mcp__x__approve")]
    [InlineData("--permission-prompt-tool=mcp__x__approve")]
    [InlineData("--agents C:\\a.json")]
    [InlineData("--plugin-dir C:\\p")]
    [InlineData("--plugin-url=https://x/p.zip")]
    [InlineData("--yolo")]
    [InlineData("--trust")]
    [InlineData("--dangerously-skip-permissions")]
    public void Claude_drops_permission_bypassing_flags_in_every_mode(string flags)
    {
        foreach (var mode in new[] { ToolMode.ChatOnly, ToolMode.Inherit })
            foreach (var args in new[] { Claude(mode, flags.Split(' ')), Claude(mode, extra: flags) })
            {
                var name = flags.Split(' ', '=')[0];
                Assert.DoesNotContain(args, a => a.StartsWith(name, StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(args, a => a is "Bash" or "Edit" or @"C:\s.json" or "mcp__x__approve" or @"C:\a.json" or @"C:\p");
            }
    }

    [Fact]
    public void Claude_inherit_keeps_the_users_own_tool_and_mcp_flags()
    {
        var args = Claude(ToolMode.Inherit, extra: @"--mcp-config C:\m.json --disallowedTools Bash --tools default");
        Assert.Equal(@"C:\m.json", args[args.IndexOf("--mcp-config") + 1]);
        Assert.Equal("Bash", args[args.IndexOf("--disallowedTools") + 1]); // restricting, never dropped
        Assert.Equal("default", args[args.IndexOf("--tools") + 1]);
        Assert.DoesNotContain("--strict-mcp-config", args);
    }

    private static List<string> Agy(ToolMode mode, IReadOnlyList<string>? args = null, string? extra = null) =>
        AgyProtocol.BuildArgs(new BackendProfile { Tools = mode, Agent = "hotline", Args = args?.ToList(), ExtraArgs = extra }).ToList();

    [Theory]
    [InlineData("--agent other")]
    [InlineData("--agent=other")]
    [InlineData("-agent other")]
    [InlineData("--add-dir C:\\")]
    [InlineData("--add-dir=C:\\")]
    public void Agy_chat_only_keeps_the_hotline_agent_whatever_the_args(string flags)
    {
        foreach (var args in new[] { Agy(ToolMode.ChatOnly, flags.Split(' ')), Agy(ToolMode.ChatOnly, extra: flags), Agy(ToolMode.ChatOnly, []) })
        {
            Assert.Equal("hotline", args[args.IndexOf("--agent") + 1]);
            Assert.Single(args, a => a.TrimStart('-').StartsWith("agent", StringComparison.Ordinal));
            Assert.DoesNotContain(args, a => a is "other" or @"C:\" || a.Contains("add-dir", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("--mode auto")]
    [InlineData("--mode=auto")]
    [InlineData("--mode accept-edits")]
    [InlineData("-mode=accept-edits")]
    [InlineData("--remote-control")]
    [InlineData("--yolo")]
    [InlineData("--trust")]
    [InlineData("--dangerously-skip-permissions")]
    [InlineData("-dangerously-skip-permissions")]
    [InlineData("--dangerously-skip-permissions=true")]
    public void Agy_drops_auto_approving_flags_in_every_mode(string flags)
    {
        foreach (var mode in new[] { ToolMode.ChatOnly, ToolMode.Inherit })
            foreach (var args in new[] { Agy(mode, flags.Split(' ')), Agy(mode, extra: flags) })
                Assert.DoesNotContain(args, a => a.Contains("mode", StringComparison.Ordinal) || a is "auto" or "accept-edits"
                                                 || a.Contains("remote", StringComparison.Ordinal) || a.Contains("yolo", StringComparison.Ordinal)
                                                 || a.Contains("trust", StringComparison.Ordinal) || a.Contains("dangerous", StringComparison.Ordinal));
    }

    [Fact]
    public void Agy_plan_mode_passes()
    {
        var args = Agy(ToolMode.ChatOnly, extra: "--mode plan");
        Assert.Equal("plan", args[args.IndexOf("--mode") + 1]);
    }

    private static List<string> Codex(ToolMode mode, IReadOnlyList<string>? args = null, string? extra = null, string? resume = null) =>
        CodexProtocol.BuildArgs(new BackendProfile { Tools = mode, Args = args?.ToList(), ExtraArgs = extra }, "x", resume, []).ToList();

    [Fact]
    public void Codex_chat_only_ignores_the_users_config_even_with_custom_args_and_before_resume()
    {
        foreach (var args in new[] { Codex(ToolMode.ChatOnly, []), Codex(ToolMode.ChatOnly, ["--search"]), Codex(ToolMode.ChatOnly, ["--ignore-user-config"], resume: "t1") })
        {
            Assert.Single(args, "--ignore-user-config");
            Assert.Single(args, "--ignore-rules");
            var resume = args.IndexOf("resume");
            if (resume >= 0) Assert.True(args.IndexOf("--ignore-user-config") < resume);
            Assert.Equal("read-only", args[args.IndexOf("--sandbox") + 1]);
        }
    }

    [Theory]
    [InlineData("--sandbox danger-full-access")]
    [InlineData("--sandbox=danger-full-access")]
    [InlineData("-s danger-full-access")]
    [InlineData("-s=danger-full-access")]
    [InlineData("--dangerously-bypass-approvals-and-sandbox")]
    [InlineData("--full-auto")]
    [InlineData("--yolo")]
    [InlineData("--trust")]
    public void Codex_drops_sandbox_loosening_flags_in_every_mode(string flags)
    {
        foreach (var mode in new[] { ToolMode.ChatOnly, ToolMode.Inherit })
            foreach (var args in new[] { Codex(mode, flags.Split(' ')), Codex(mode, extra: flags) })
            {
                Assert.DoesNotContain(args, a => a is "danger-full-access" or "--full-auto" or "--yolo" or "--trust" || a.Contains("dangerous", StringComparison.Ordinal));
                Assert.Single(args, "--sandbox");
            }
    }

    [Theory]
    [InlineData("-p work")]
    [InlineData("--profile=work")]
    [InlineData("-c mcp_servers.x.command=\"calc\"")]
    [InlineData("--config=mcp_servers.x.command=\"calc\"")]
    public void Codex_chat_only_drops_profiles_and_mcp_servers(string flags)
    {
        foreach (var args in new[] { Codex(ToolMode.ChatOnly, flags.Split(' ')), Codex(ToolMode.ChatOnly, extra: flags) })
            Assert.DoesNotContain(args, a => a is "-p" or "work" || a.Contains("profile", StringComparison.Ordinal) || a.Contains("mcp_servers", StringComparison.Ordinal));
        Assert.Contains(Codex(ToolMode.Inherit, extra: flags), a => a.Contains("work", StringComparison.Ordinal) || a.Contains("mcp_servers", StringComparison.Ordinal));
    }
}
