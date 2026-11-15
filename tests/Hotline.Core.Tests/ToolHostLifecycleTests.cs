using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Api;
using Hotline.Core.Diagnostics;
using Hotline.Core.State;
using Hotline.Core.Tools;

namespace Hotline.Core.Tests;

/// <summary>McpToolHost lifetime: cached tool lists, per-server restarts, start timeouts, failure backoff, grants, and launch secrets.</summary>
public sealed class ToolHostLifecycleTests : TempDirTest
{
    private string McpPath => Path.Combine(_dir, "mcp.json");
    private string StatePath => Path.Combine(_dir, "state.json");
    private string LogPath => Path.Combine(_dir, "h.log");
    private FileLog Log => new(LogPath);

    private static IReadOnlyDictionary<string, ToolApproval> EmptyRules() =>
        new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase);

    private void WriteConfig(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(McpPath, json);
    }

    private McpToolHost CreateHost(
        Func<IReadOnlyList<McpServerConfig>> extraServers,
        Func<McpServerConfig, CancellationToken, Task<IMcpSession>> connect,
        Func<ToolCallRequest, CancellationToken, Task<ToolDecision>> approve,
        Func<IReadOnlyDictionary<string, ToolApproval>>? rules = null,
        IGrantStore? grants = null,
        ISecretStore? secrets = null,
        TimeProvider? time = null) =>
        new(() => McpConfig.Load(McpPath), extraServers, rules ?? EmptyRules,
            grants ?? new StateStore(StatePath), secrets ?? new InMemorySecretStore(),
            connect, approve, Log, time);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_removed_server_is_not_called_from_a_stale_tool_list(bool extra)
    {
        WriteConfig(extra ? "{}" : """{"mcpServers":{"a":{"command":"x"}}}""");
        var extras = extra ? new[] { new McpServerConfig("a", "x", [], new Dictionary<string, string>()) } : [];
        var session = new FakeMcpSession(("read", true));
        await using var host = CreateHost(() => extras, (_, _) => Task.FromResult<IMcpSession>(session),
            (_, _) => throw new Exception("read-only must not ask"));
        var tool = Assert.Single(await host.GetToolsAsync(TestContext.Current.CancellationToken));
        WriteConfig("{}");
        extras = [];
        var result = await host.CallAsync(tool, JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Equal(0, session.Calls);
    }

    [Theory]
    [InlineData("""{"mcpServers":{"a":{"command":"x","disabled":true}}}""", null, ToolDecision.AllowOnce)]
    [InlineData("{}", null, ToolDecision.AllowOnce)]
    [InlineData("""{"mcpServers":{"a":{"command":"x"}}}""", "a/write=deny", ToolDecision.AllowOnce)]
    [InlineData("""{"mcpServers":{"a":{"command":"x","disabled":true}}}""", null, ToolDecision.AllowAlways)]
    [InlineData("{}", null, ToolDecision.AllowAlways)]
    [InlineData("""{"mcpServers":{"a":{"command":"x"}}}""", "a/write=deny", ToolDecision.AllowAlways)]
    [InlineData("""{"mcpServers":{"a":{"command":"x2"}}}""", null, ToolDecision.AllowAlways)] // launch hash changed mid-answer
    public async Task Config_or_rule_changes_while_approval_is_pending_prevent_the_call_and_grant(string changed, string? denyRule, ToolDecision answer)
    {
        WriteConfig("""{"mcpServers":{"a":{"command":"x"}}}""");
        var rules = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase);
        var state = new StateStore(StatePath);
        var pending = new TaskCompletionSource<ToolDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeMcpSession(("write", false));
        await using var host = CreateHost(() => [], (_, _) => Task.FromResult<IMcpSession>(session),
            (_, _) => { asked.SetResult(); return pending.Task; }, () => rules, state);
        var tool = Assert.Single(await host.GetToolsAsync(TestContext.Current.CancellationToken));
        var call = host.CallAsync(tool, JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        WriteConfig(changed);
        if (denyRule is not null)
        {
            var parts = denyRule.Split('=');
            rules[parts[0]] = Enum.Parse<ToolApproval>(parts[1], ignoreCase: true);
        }
        pending.SetResult(answer);
        var result = await call;
        Assert.True(result.IsError);
        Assert.Equal(0, session.Calls);
        Assert.Empty(state.GetGrants());
    }

    [Fact]
    public async Task A_saved_grant_persists_across_hosts_and_can_be_revoked_without_restarting()
    {
        const string config = """{ "mcpServers": { "a": { "command": "x", "args": ["--ok"], "cwd": "C:\\work" } } }""";
        WriteConfig(config);
        var state = new StateStore(StatePath);
        var launch = StateStore.ComputeLaunchHash("x", ["--ok"], @"C:\work");
        var asked = 0;
        await using (var host1 = CreateHost(() => [], (_, _) => Task.FromResult<IMcpSession>(new FakeMcpSession(("write", false))),
            (_, _) => { asked++; return Task.FromResult(ToolDecision.AllowAlways); }, grants: state))
        {
            var tool = Assert.Single(await host1.GetToolsAsync(TestContext.Current.CancellationToken));
            using var args = JsonDocument.Parse("{}");
            await host1.CallAsync(tool, args.RootElement, TestContext.Current.CancellationToken);
            Assert.Equal(config, File.ReadAllText(McpPath)); // mcp.json untouched
        }

        // New host picks up persisted grant from state.json, then revoke takes effect immediately without restarting.
        await using var host2 = CreateHost(() => [], (_, _) => Task.FromResult<IMcpSession>(new FakeMcpSession(("write", false))),
            (_, _) => { asked++; return Task.FromResult(ToolDecision.AllowOnce); }, grants: new StateStore(StatePath));
        var tool2 = Assert.Single(await host2.GetToolsAsync(TestContext.Current.CancellationToken));
        using var emptyArgs = JsonDocument.Parse("{}");
        await host2.CallAsync(tool2, emptyArgs.RootElement, TestContext.Current.CancellationToken);
        Assert.Equal(1, asked);

        Assert.True(new StateStore(StatePath).RevokeGrant("a", "write", launch));
        await host2.CallAsync(tool2, emptyArgs.RootElement, TestContext.Current.CancellationToken);
        Assert.Equal(2, asked);
    }

    [Theory]
    [InlineData("""{ "mcpServers": { "a": { "command": "y", "args": ["--ok"], "cwd": "C:\\work" } } }""")]
    [InlineData("""{ "mcpServers": { "a": { "command": "x", "args": ["--changed"], "cwd": "C:\\work" } } }""")]
    [InlineData("""{ "mcpServers": { "a": { "command": "x", "args": ["--ok"], "cwd": "C:\\other" } } }""")]
    public async Task Changing_command_args_or_cwd_invalidates_existing_grant(string changedConfig)
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x", "args": ["--ok"], "cwd": "C:\\work" } } }""");
        var state = new StateStore(StatePath);
        state.AddGrant("a", "write", StateStore.ComputeLaunchHash("x", ["--ok"], @"C:\work"));
        WriteConfig(changedConfig);
        var asked = 0;
        await using var host = CreateHost(() => [], (_, _) => Task.FromResult<IMcpSession>(new FakeMcpSession(("write", false))),
            (_, _) => { asked++; return Task.FromResult(ToolDecision.Deny); }, grants: state);
        var tool = Assert.Single(await host.GetToolsAsync(TestContext.Current.CancellationToken));
        var result = await host.CallAsync(tool, JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Equal(1, asked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Config_edited_to_another_command_after_start_refuses_call_and_stores_no_grant(bool staleNewHashGrant)
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "old-cmd" } } }""");
        var state = new StateStore(StatePath);
        if (staleNewHashGrant)
            state.AddGrant("a", "write", StateStore.ComputeLaunchHash("new-cmd"));
        var asked = 0;
        var session = new FakeMcpSession(("write", false));
        await using var host = CreateHost(() => [], (_, _) => Task.FromResult<IMcpSession>(session),
            (_, _) => { asked++; return Task.FromResult(ToolDecision.AllowAlways); }, grants: state);
        var tool = Assert.Single(await host.GetToolsAsync(TestContext.Current.CancellationToken));

        // Edit command on disk BEFORE the next GetToolsAsync refresh: old process stays in snapshot,
        // so stale grants for new-cmd must NOT run on it and Always allow must NOT store for either hash.
        WriteConfig("""{ "mcpServers": { "a": { "command": "new-cmd" } } }""");
        var result = await host.CallAsync(tool, JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Equal(0, session.Calls);
        Assert.Equal(0, asked);
        Assert.False(state.IsGranted("a", "write", StateStore.ComputeLaunchHash("old-cmd")));
    }

    [Fact]
    public async Task Server_errors_and_stderr_scrub_resolved_secrets_and_long_env_values()
    {
        WriteConfig("""
            {
              "mcpServers": {
                "bad": {
                  "command": "x",
                  "env": { "SHORT_SEC": "${hotline:secret:s3}", "LONG_SEC": "${hotline:secret:s_long}", "LONG_ENV": "12345678_plain", "SHORT_ENV": "keepme" }
                }
              }
            }
            """);
        var secrets = new InMemorySecretStore();
        secrets.Set("mcp:secret:s3", "xyz");
        secrets.Set("mcp:secret:s_long", "LONG_SECRET_999");

        await using var startFailHost = CreateHost(() => [],
            (_, _) => throw new IOException("connect failed with xyz LONG_SECRET_999 12345678_plain keepme"),
            (_, _) => Task.FromResult(ToolDecision.Deny), secrets: secrets);
        await startFailHost.GetToolsAsync(TestContext.Current.CancellationToken);
        var statusErr = Assert.Single(startFailHost.Status).Error!;
        Assert.Contains("***", statusErr);
        Assert.Contains("keepme", statusErr);
        Assert.DoesNotContain("xyz", statusErr);
        Assert.DoesNotContain("LONG_SECRET_999", statusErr);
        Assert.DoesNotContain("12345678_plain", statusErr);
        var logText = File.ReadAllText(LogPath);
        Assert.DoesNotContain("xyz", logText);
        Assert.DoesNotContain("LONG_SECRET_999", logText);
        Assert.DoesNotContain("12345678_plain", logText);

        // Call-time session failure and StderrThrottle tail + verbose lines also scrub values.
        await using var callFailHost = CreateHost(() => [],
            (_, _) => Task.FromResult<IMcpSession>(new ThrowingCallSession("boom xyz LONG_SECRET_999")),
            (_, _) => Task.FromResult(ToolDecision.Deny), secrets: secrets);
        var tool = Assert.Single(await callFailHost.GetToolsAsync(TestContext.Current.CancellationToken));
        var callRes = await callFailHost.CallAsync(tool, JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);
        Assert.True(callRes.IsError);
        Assert.DoesNotContain("xyz", callRes.Text + File.ReadAllText(LogPath));
        Assert.DoesNotContain("LONG_SECRET_999", callRes.Text + File.ReadAllText(LogPath));

        var stderrLines = new List<string>();
        var throttle = new StderrThrottle("bad", stderrLines.Add, new ManualTimeProvider(), verbose: () => true,
            scrub: text => McpSecrets.Scrub(text, ["xyz", "LONG_SECRET_999", "12345678_plain"]));
        throttle.Line("err xyz LONG_SECRET_999 12345678_plain keepme");
        Assert.DoesNotContain("xyz", throttle.Tail);
        Assert.DoesNotContain("LONG_SECRET_999", throttle.Tail);
        Assert.DoesNotContain("12345678_plain", throttle.Tail);
        Assert.Contains("***", Assert.Single(stderrLines));
        Assert.Contains("keepme", Assert.Single(stderrLines));
    }

    private sealed class ThrowingCallSession(string message) : IMcpSession
    {
        public Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<McpToolInfo>>([new McpToolInfo("read", "", JsonDocument.Parse("{}").RootElement, true)]);
        public Task<ToolResult> CallAsync(string tool, JsonElement arguments, CancellationToken ct) =>
            throw new IOException(message);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Rule_overrides_existing_grant_without_widening()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" } } }""");
        var state = new StateStore(StatePath);
        state.AddGrant("a", "write", StateStore.ComputeLaunchHash("x"));
        var rules = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase) { ["a/*"] = ToolApproval.Ask };
        var asked = 0;
        bool? sawAllowAlways = null;
        await using var host = CreateHost(() => [], (_, _) => Task.FromResult<IMcpSession>(new FakeMcpSession(("write", false))),
            (req, _) => { asked++; sawAllowAlways = req.AllowAlways; return Task.FromResult(ToolDecision.AllowOnce); },
            () => rules, state);
        var tool = Assert.Single(await host.GetToolsAsync(TestContext.Current.CancellationToken));
        var result = await host.CallAsync(tool, JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);
        Assert.False(result.IsError);
        Assert.Equal(1, asked);
        Assert.False(sawAllowAlways);

        // Removing the rule lets the grant apply again for the matching launch hash.
        rules.Clear();
        await host.CallAsync(tool, JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Secret_tokens_resolve_at_launch_and_bad_or_missing_tokens_block_only_that_server()
    {
        WriteConfig("""
            {
              "mcpServers": {
                "good": { "command": "x", "env": { "TOK": "${hotline:secret:good_key}", "PLAIN": "PLAIN_ENV_VAL" } },
                "missing": { "command": "y", "env": { "TOK": "${hotline:secret:later_key}" } },
                "malformed": { "command": "z", "env": { "TOK": "${hotline:secret:bad space}" } }
              }
            }
            """);
        var secrets = new InMemorySecretStore();
        secrets.Set("mcp:secret:good_key", "REAL_SECRET_XYZ");
        var seenEnv = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        await using var host = CreateHost(() => [], (server, _) =>
            {
                seenEnv[server.Name] = server.Env;
                return Task.FromResult<IMcpSession>(new FakeMcpSession((server.Name + "_tool", true)));
            },
            (_, _) => Task.FromResult(ToolDecision.Deny), secrets: secrets);

        var tools = await host.GetToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["good_tool"], tools.Select(t => t.Tool));
        Assert.Equal("REAL_SECRET_XYZ", seenEnv["good"]["TOK"]);
        Assert.Equal("PLAIN_ENV_VAL", seenEnv["good"]["PLAIN"]);
        Assert.False(seenEnv.ContainsKey("missing"));
        Assert.False(seenEnv.ContainsKey("malformed"));

        Assert.Contains(host.Status, s => s.Name == "missing" && s.State == McpServerState.Failed && s.Error!.Contains("later_key"));
        Assert.Contains(host.Status, s => s.Name == "malformed" && s.State == McpServerState.Failed);

        // Neither secret values nor env values ever appear in logs or status.
        var allStatus = string.Join("\n", host.Status.Select(s => s.Error ?? ""));
        var logText = File.Exists(LogPath) ? File.ReadAllText(LogPath) : "";
        Assert.DoesNotContain("REAL_SECRET_XYZ", allStatus + logText);
        Assert.DoesNotContain("bad space", allStatus + logText);

        // Adding the missing secret + RetryFailedServers starts the missing server cleanly.
        secrets.Set("mcp:secret:later_key", "SECOND_SECRET");
        host.RetryFailedServers();
        tools = await host.GetToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["good_tool", "missing_tool"], tools.Select(t => t.Tool));
        Assert.Equal("SECOND_SECRET", seenEnv["missing"]["TOK"]);
    }

    [Fact]
    public async Task A_server_switched_off_mid_answer_is_not_called()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" } } }""");
        var session = new FakeMcpSession(("read", true));
        await using var host = CreateHost(() => [], (_, _) => Task.FromResult<IMcpSession>(session),
            (_, _) => Task.FromResult(ToolDecision.Deny));
        var tool = Assert.Single(await host.GetToolsAsync(TestContext.Current.CancellationToken));
        WriteConfig("""{ "mcpServers": { "a": { "command": "x", "disabled": true } } }""");
        using var args = JsonDocument.Parse("{}");
        var result = await host.CallAsync(tool, args.RootElement, TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
    }

    [Fact]
    public void Stderr_is_hidden_by_default_and_reports_only_counts_and_skipped_lines()
    {
        var clock = new ManualTimeProvider();
        var lines = new List<string>();
        var throttle = new StderrThrottle("fs", lines.Add, clock, perMinute: 3);
        for (var i = 0; i < 10; i++) throttle.Line("token=FAKE_SECRET_123456 user document text");
        Assert.Equal(["fs: 1 stderr lines (hidden: turn on verbose logging to see them)"], lines);
        clock.Advance(TimeSpan.FromMinutes(1));
        throttle.Line("more user text");
        Assert.Equal(["fs: 1 stderr lines (hidden: turn on verbose logging to see them)", "fs: (7 stderr lines skipped)",
            "fs: 10 stderr lines (hidden: turn on verbose logging to see them)"], lines);
    }

    [Fact]
    public void Verbose_stderr_is_cleaned_capped_and_changes_with_the_live_flag()
    {
        var lines = new List<string>();
        var verbose = false;
        var throttle = new StderrThrottle("fs", lines.Add, new ManualTimeProvider(), verbose: () => verbose);
        const string content = "token=FAKE_SECRET_123456 user document\u001b text ";
        throttle.Line(content);
        Assert.DoesNotContain("user document", Assert.Single(lines));
        verbose = true;
        throttle.Line(content + new string('x', 200));
        Assert.Equal("fs: stderr line 2: " + ("token=[redacted] user document? text " + new string('x', 200))[..120], lines[1]);
        Assert.DoesNotContain("FAKE_SECRET_123456", lines[1]);
        verbose = false;
        throttle.Line(content);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public async Task Tool_lists_are_cached_and_only_a_changed_server_restarts()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" }, "b": { "command": "y" } } }""");
        var sessions = new List<(string Server, FakeMcpSession Session)>();
        await using var host = CreateHost(() => [],
            (server, _) => { var s = new FakeMcpSession((server.Name + "_tool", true)); sessions.Add((server.Name, s)); return Task.FromResult<IMcpSession>(s); },
            (_, _) => Task.FromResult(ToolDecision.Deny));

        await host.GetToolsAsync(TestContext.Current.CancellationToken);
        await host.GetToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Equal(1, s.Session.Lists)); // listed once, not per answer

        WriteConfig("""{ "mcpServers": { "a": { "command": "x" }, "b": { "command": "y2" } } }""");
        var tools = await host.GetToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, sessions.Count);
        Assert.False(sessions[0].Session.Disposed); // a untouched
        Assert.True(sessions[1].Session.Disposed);  // old b stopped
        Assert.Equal(["a_tool", "b_tool"], tools.Select(t => t.Tool));
    }

    [Fact]
    public async Task Extra_servers_arriving_later_do_not_restart_existing_ones()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" } } }""");
        IReadOnlyList<McpServerConfig> extra = [];
        var sessions = new List<FakeMcpSession>();
        await using var host = CreateHost(() => extra,
            (server, _) => { var s = new FakeMcpSession((server.Name, true)); sessions.Add(s); return Task.FromResult<IMcpSession>(s); },
            (_, _) => Task.FromResult(ToolDecision.Deny));
        await host.GetToolsAsync(TestContext.Current.CancellationToken);
        extra = [new McpServerConfig("windows-x", "odr", [], new Dictionary<string, string>())];
        var tools = await host.GetToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, tools.Count);
        Assert.False(sessions[0].Disposed);
    }

    [Fact]
    public async Task A_server_that_never_lists_times_out_instead_of_hanging()
    {
        WriteConfig("""{ "mcpServers": { "slow": { "command": "x" }, "ok": { "command": "y" } } }""");
        var slow = new FakeMcpSession(("s", true)) { ListDelay = TimeSpan.FromMinutes(5) };
        await using var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [], EmptyRules,
            new StateStore(StatePath), new InMemorySecretStore(),
            (server, _) => Task.FromResult<IMcpSession>(server.Name == "slow" ? slow : new FakeMcpSession(("fast", true))),
            (_, _) => Task.FromResult(ToolDecision.Deny), Log) { StartTimeout = TimeSpan.FromMilliseconds(200) };

        var tools = await host.GetToolsAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(["fast"], tools.Select(t => t.Tool));
        Assert.True(slow.Disposed);
        Assert.Contains(host.Status, s => s.Name == "slow" && s.State == McpServerState.Failed && s.Error!.Contains("didn't start"));
    }

    [Fact]
    public async Task A_failed_server_is_retried_after_a_minute_or_on_request_not_every_answer()
    {
        WriteConfig("""{ "mcpServers": { "bad": { "command": "x" } } }""");
        var clock = new ManualTimeProvider();
        var attempts = 0;
        await using var host = CreateHost(() => [],
            (_, _) => { attempts++; throw new InvalidOperationException("nope"); },
            (_, _) => Task.FromResult(ToolDecision.Deny), time: clock);

        await host.GetToolsAsync(TestContext.Current.CancellationToken);
        await host.GetToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, attempts);
        Assert.Contains(host.Status, s => s.Name == "bad" && s.State == McpServerState.Failed);
        clock.Advance(TimeSpan.FromMinutes(2));
        await host.GetToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, attempts);
        host.RetryFailedServers();
        await host.GetToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, attempts);
    }

    [Theory]
    [InlineData("1password", "get_item", "_1password__get_item")]
    [InlineData("-tools", "x", "_-tools__x")]
    [InlineData("files", "read", "files__read")]
    public void Api_names_start_with_a_letter_or_underscore(string server, string tool, string expected)
        => Assert.Equal(expected, ToolNames.ForApi(server, tool, new HashSet<string>()));

    [Fact]
    public void Replayed_answers_lose_hotlines_tool_notes()
    {
        var answer = "Let me look.\n\n> 🔧 files › read_file\n\n> ⚠ not found\n\nIt says hi.\n\n> quoted by the model";
        Assert.Equal("Let me look.\n\nIt says hi.\n\n> quoted by the model", ToolLoop.StripNotes(answer));
        Assert.Equal("plain", ToolLoop.StripNotes("plain"));
    }

    [Fact]
    public async Task Bad_arguments_go_back_to_the_model_instead_of_running_the_tool()
    {
        var host = new ToolLoopTests.FakeToolHost();
        var offered = await host.GetToolsAsync(TestContext.Current.CancellationToken);
        var broken = await ToolLoop.CallAsync(host, offered, "files__read_file", "{\"path\":", TestContext.Current.CancellationToken);
        var notObject = await ToolLoop.CallAsync(host, offered, "files__read_file", "[1]", TestContext.Current.CancellationToken);
        Assert.True(broken.IsError && notObject.IsError);
        Assert.Empty(host.Calls);
        await ToolLoop.CallAsync(host, offered, "files__read_file", "", TestContext.Current.CancellationToken); // empty = no arguments
        Assert.Single(host.Calls);
        Assert.Equal("(no output)", ToolLoop.ResultText(new ToolResult("", false)));
    }

    [Fact]
    public async Task Names_the_model_was_not_offered_go_nowhere()
    {
        var host = new ToolLoopTests.FakeToolHost();
        var result = await ToolLoop.CallAsync(host, [], "files__read_file", "{}", TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Empty(host.Calls);
    }

    [Fact]
    public void A_deny_always_wins_over_an_older_allow()
    {
        var rules = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase) { ["files/write"] = ToolApproval.Allow, ["files/*"] = ToolApproval.Deny };
        Assert.Equal(ToolApproval.Deny, ToolPolicy.Decide(rules, "files", "write", false));
        rules = new(StringComparer.OrdinalIgnoreCase) { ["files/write"] = ToolApproval.Deny, ["files/*"] = ToolApproval.Allow };
        Assert.Equal(ToolApproval.Deny, ToolPolicy.Decide(rules, "files", "write", true));
        rules = new(StringComparer.OrdinalIgnoreCase) { ["files/write"] = ToolApproval.Ask, ["files/*"] = ToolApproval.Allow };
        Assert.Equal(ToolApproval.Ask, ToolPolicy.Decide(rules, "files", "write", true));
    }

    [Fact]
    public async Task An_unreadable_mcp_json_keeps_the_last_good_servers_and_applies_live_rules()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" } } }""");
        var rules = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase) { ["a/secret"] = ToolApproval.Deny };
        var session = new FakeMcpSession(("open", true), ("secret", true));
        await using var host = CreateHost(() => [], (_, _) => Task.FromResult<IMcpSession>(session),
            (_, _) => Task.FromResult(ToolDecision.Deny), () => rules);
        Assert.Equal(["open"], (await host.GetToolsAsync(TestContext.Current.CancellationToken)).Select(t => t.Tool));

        File.WriteAllText(McpPath, "{ half-saved");
        Assert.Equal(["open"], (await host.GetToolsAsync(TestContext.Current.CancellationToken)).Select(t => t.Tool)); // the deny rule still applies
        Assert.False(session.Disposed);                                                  // and the server kept running
        Assert.Contains("last good", host.ConfigError);
    }

    [Fact]
    public async Task With_no_good_mcp_json_yet_no_tools_are_offered()
    {
        WriteConfig("{ broken");
        var connects = 0;
        await using var host = CreateHost(() => [new McpServerConfig("windows-x", "odr", [], new Dictionary<string, string>())],
            (_, _) => { connects++; return Task.FromResult<IMcpSession>(new FakeMcpSession(("t", true))); },
            (_, _) => Task.FromResult(ToolDecision.Deny));
        Assert.Empty(await host.GetToolsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, connects);
    }

    [Fact]
    public async Task Tools_and_servers_with_slash_or_star_names_are_skipped()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" }, "b/c": { "command": "y" } } }""");
        await using var host = CreateHost(() => [],
            (_, _) => Task.FromResult<IMcpSession>(new FakeMcpSession(("*", true), ("x/y", true), ("fine", true))),
            (_, _) => Task.FromResult(ToolDecision.Deny));
        var tools = await host.GetToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal([("a", "fine")], tools.Select(t => (t.Server, t.Tool)));
        Assert.Contains(host.Status, s => s.Name == "b/c" && s.State == McpServerState.Failed);
    }

    [Fact]
    public async Task A_dead_server_is_restarted_on_the_next_answer()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" } } }""");
        var sessions = new List<FakeMcpSession>();
        await using var host = CreateHost(() => [],
            (_, _) => { var s = new FakeMcpSession(("read", true)) { Throw = sessions.Count == 0 }; sessions.Add(s); return Task.FromResult<IMcpSession>(s); },
            (_, _) => Task.FromResult(ToolDecision.Deny));
        var tools = await host.GetToolsAsync(TestContext.Current.CancellationToken);
        Assert.True((await host.CallAsync(tools[0], JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken)).IsError);
        tools = await host.GetToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, sessions.Count);
        Assert.True(sessions[0].Disposed);
        Assert.False((await host.CallAsync(tools[0], JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken)).IsError);
    }

    [Fact]
    public async Task After_dispose_no_servers_start_again()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" } } }""");
        var host = CreateHost(() => [], (_, _) => Task.FromResult<IMcpSession>(new FakeMcpSession(("t", true))),
            (_, _) => Task.FromResult(ToolDecision.Deny));
        await host.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.GetToolsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_tool_that_stopped_being_read_only_asks_even_with_an_old_spec()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" } } }""");
        var readOnly = true;
        var asked = 0;
        await using var host = CreateHost(() => [],
            (_, _) => Task.FromResult<IMcpSession>(new FakeMcpSession(("t", true)) { ReadOnlyNow = () => readOnly }),
            (_, _) => { asked++; return Task.FromResult(ToolDecision.Deny); });
        var old = (await host.GetToolsAsync(TestContext.Current.CancellationToken))[0];
        readOnly = false;
        File.WriteAllText(McpPath, """{ "mcpServers": { "a": { "command": "x", "args": ["v2"] } } }"""); // restart -> fresh list
        await host.GetToolsAsync(TestContext.Current.CancellationToken);
        var result = await host.CallAsync(old, JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);
        Assert.Equal(1, asked);
        Assert.True(result.IsError);
    }
}
