using System.Text.Json;
using Hotline.Core.Backends.Api;
using Hotline.Core.Diagnostics;
using Hotline.Core.Tools;

namespace Hotline.Core.Tests;

/// <summary>McpToolHost lifetime: cached tool lists, per-server restarts, start timeouts, failure backoff; tool helpers.</summary>
public sealed class ToolHostLifecycleTests : TempDirTest
{
    private string McpPath => Path.Combine(_dir, "mcp.json");
    private FileLog Log => new(Path.Combine(_dir, "h.log"));

    /// <summary>One scripted MCP server: the tools it lists, a slow or failing connection, a read-only flag that can flip.</summary>
    private sealed class FakeSession(params (string Name, bool ReadOnly)[] tools) : IMcpSession
    {
        public int Lists;
        public int Calls;
        public bool Disposed;
        public bool Throw;
        public TimeSpan ListDelay;
        public Func<bool>? ReadOnlyNow;
        public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct)
        {
            Lists++;
            if (ListDelay > TimeSpan.Zero) await Task.Delay(ListDelay, ct);
            return tools.Select(t => new McpToolInfo(t.Name, "", JsonDocument.Parse("""{"type":"object"}""").RootElement, ReadOnlyNow?.Invoke() ?? t.ReadOnly)).ToList();
        }
        public Task<ToolResult> CallAsync(string t, JsonElement args, CancellationToken ct)
        {
            Calls++;
            return Throw ? throw new IOException("pipe closed") : Task.FromResult(new ToolResult("ok", false));
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private void WriteConfig(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(McpPath, json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_removed_server_is_not_called_from_a_stale_tool_list(bool extra)
    {
        WriteConfig(extra ? "{}" : """{"mcpServers":{"a":{"command":"x"}}}""");
        var extras = extra ? new[] { new McpServerConfig("a", "x", [], new Dictionary<string, string>()) } : [];
        var session = new FakeSession(("read", true));
        await using var host = new McpToolHost(() => McpConfig.Load(McpPath), () => extras,
            (_, _) => Task.FromResult<IMcpSession>(session), (_, _) => throw new Exception("read-only must not ask"), Log);
        var tool = Assert.Single(await host.GetToolsAsync(default));
        WriteConfig("{}");
        extras = [];
        var result = await host.CallAsync(tool, JsonDocument.Parse("{}").RootElement, default);
        Assert.True(result.IsError);
        Assert.Equal(0, session.Calls);
    }

    [Theory]
    [InlineData("""{"mcpServers":{"a":{"command":"x","disabled":true}}}""", ToolDecision.AllowOnce)]
    [InlineData("{}", ToolDecision.AllowOnce)]
    [InlineData("""{"mcpServers":{"a":{"command":"x"}},"approvals":{"a/write":"deny"}}""", ToolDecision.AllowOnce)]
    [InlineData("""{"mcpServers":{"a":{"command":"x","disabled":true}}}""", ToolDecision.AllowAlways)]
    [InlineData("{}", ToolDecision.AllowAlways)]
    [InlineData("""{"mcpServers":{"a":{"command":"x"}},"approvals":{"a/write":"deny"}}""", ToolDecision.AllowAlways)]
    public async Task Config_changes_while_approval_is_pending_prevent_the_call(string changed, ToolDecision answer)
    {
        WriteConfig("""{"mcpServers":{"a":{"command":"x"}}}""");
        var pending = new TaskCompletionSource<ToolDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeSession(("write", false));
        await using var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (_, _) => Task.FromResult<IMcpSession>(session),
            (_, _) => { asked.SetResult(); return pending.Task; }, Log, McpPath);
        var tool = Assert.Single(await host.GetToolsAsync(default));
        var call = host.CallAsync(tool, JsonDocument.Parse("{}").RootElement, default);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        WriteConfig(changed);
        pending.SetResult(answer);
        var result = await call;
        Assert.True(result.IsError);
        Assert.Equal(0, session.Calls);
        Assert.Equal(changed, File.ReadAllText(McpPath));
    }

    [Fact]
    public async Task A_saved_always_approval_can_be_removed_without_restarting()
    {
        const string config = """{ "mcpServers": { "a": { "command": "x" } } }""";
        WriteConfig(config);
        var asked = 0;
        await using var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (_, _) => Task.FromResult<IMcpSession>(new FakeSession(("write", false))),
            (_, _) => { asked++; return Task.FromResult(ToolDecision.AllowAlways); }, Log, McpPath);
        var tool = Assert.Single(await host.GetToolsAsync(TestContext.Current.CancellationToken));
        using var args = JsonDocument.Parse("{}");
        await host.CallAsync(tool, args.RootElement, TestContext.Current.CancellationToken);
        Assert.Equal(ToolApproval.Allow, McpConfig.Load(McpPath).Approvals["a/write"]);
        await host.CallAsync(tool, args.RootElement, TestContext.Current.CancellationToken);
        Assert.Equal(1, asked);
        WriteConfig(config);
        await host.CallAsync(tool, args.RootElement, TestContext.Current.CancellationToken);
        Assert.Equal(2, asked);
    }

    [Fact]
    public async Task A_server_switched_off_mid_answer_is_not_called()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" } } }""");
        var session = new FakeSession(("read", true));
        await using var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (_, _) => Task.FromResult<IMcpSession>(session),
            (_, _) => Task.FromResult(ToolDecision.Deny), Log, McpPath);
        var tool = Assert.Single(await host.GetToolsAsync(TestContext.Current.CancellationToken));
        WriteConfig("""{ "mcpServers": { "a": { "command": "x", "disabled": true } } }""");
        using var args = JsonDocument.Parse("{}");
        var result = await host.CallAsync(tool, args.RootElement, TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
    }

    [Theory]
    [InlineData("a/write", "ask")]
    [InlineData("a/write", "allow")]
    [InlineData("a/write", "deny")]
    [InlineData("a/*", "ask")]
    [InlineData("a/*", "deny")]
    [InlineData("removed", "")]
    [InlineData("disabled", "")]
    public async Task Failed_approval_saves_are_remembered_until_a_file_rule_or_server_removal(string key, string rule)
    {
        const string config = """{ "mcpServers": { "a": { "command": "x" } } }""";
        WriteConfig(config);
        var blocked = Path.Combine(_dir, "directory-not-file");
        Directory.CreateDirectory(blocked);
        var asked = 0;
        await using var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (_, _) => Task.FromResult<IMcpSession>(new FakeSession(("write", false))),
            (_, _) => { asked++; return Task.FromResult(asked == 1 ? ToolDecision.AllowAlways : ToolDecision.AllowOnce); }, Log, blocked);
        var tool = Assert.Single(await host.GetToolsAsync(TestContext.Current.CancellationToken));
        using var args = JsonDocument.Parse("{}");
        await host.CallAsync(tool, args.RootElement, TestContext.Current.CancellationToken);
        await host.CallAsync(tool, args.RootElement, TestContext.Current.CancellationToken);
        Assert.Equal(1, asked); // the failed save still allows for this process

        if (key is "removed" or "disabled")
        {
            WriteConfig(key == "removed" ? "{}" : """{ "mcpServers": { "a": { "command": "x", "disabled": true } } }""");
            Assert.Empty(await host.GetToolsAsync(TestContext.Current.CancellationToken));
        }
        else
        {
            WriteConfig($$"""{ "mcpServers": { "a": { "command": "x" } }, "approvals": { "{{key}}": "{{rule}}" } }""");
            var result = await host.CallAsync(tool, args.RootElement, TestContext.Current.CancellationToken);
            Assert.Equal(rule == "deny", result.IsError);
            Assert.Equal(rule == "ask" ? 2 : 1, asked);
        }
        WriteConfig(config);
        tool = Assert.Single(await host.GetToolsAsync(TestContext.Current.CancellationToken));
        var before = asked;
        await host.CallAsync(tool, args.RootElement, TestContext.Current.CancellationToken);
        Assert.Equal(before + 1, asked); // a removed file rule cannot revive an old fallback
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
        var sessions = new List<(string Server, FakeSession Session)>();
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (server, _) => { var s = new FakeSession((server.Name + "_tool", true)); sessions.Add((server.Name, s)); return Task.FromResult<IMcpSession>(s); },
            (_, _) => Task.FromResult(ToolDecision.Deny), Log);

        await host.GetToolsAsync(default);
        await host.GetToolsAsync(default);
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Equal(1, s.Session.Lists)); // listed once, not per answer

        WriteConfig("""{ "mcpServers": { "a": { "command": "x" }, "b": { "command": "y2" } } }""");
        var tools = await host.GetToolsAsync(default);
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
        var sessions = new List<FakeSession>();
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => extra,
            (server, _) => { var s = new FakeSession((server.Name, true)); sessions.Add(s); return Task.FromResult<IMcpSession>(s); },
            (_, _) => Task.FromResult(ToolDecision.Deny), Log);
        await host.GetToolsAsync(default);
        extra = [new McpServerConfig("windows-x", "odr", [], new Dictionary<string, string>())];
        var tools = await host.GetToolsAsync(default);
        Assert.Equal(2, tools.Count);
        Assert.False(sessions[0].Disposed);
    }

    [Fact]
    public async Task A_server_that_never_lists_times_out_instead_of_hanging()
    {
        WriteConfig("""{ "mcpServers": { "slow": { "command": "x" }, "ok": { "command": "y" } } }""");
        var slow = new FakeSession(("s", true)) { ListDelay = TimeSpan.FromMinutes(5) };
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (server, _) => Task.FromResult<IMcpSession>(server.Name == "slow" ? slow : new FakeSession(("fast", true))),
            (_, _) => Task.FromResult(ToolDecision.Deny), Log) { StartTimeout = TimeSpan.FromMilliseconds(200) };

        var tools = await host.GetToolsAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
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
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (_, _) => { attempts++; throw new InvalidOperationException("nope"); },
            (_, _) => Task.FromResult(ToolDecision.Deny), Log, time: clock);

        await host.GetToolsAsync(default);
        await host.GetToolsAsync(default);
        Assert.Equal(1, attempts);
        Assert.Contains(host.Status, s => s.Name == "bad" && s.State == McpServerState.Failed);
        clock.Advance(TimeSpan.FromMinutes(2));
        await host.GetToolsAsync(default);
        Assert.Equal(2, attempts);
        host.RetryFailedServers();
        await host.GetToolsAsync(default);
        Assert.Equal(3, attempts);
    }

    [Theory]
    [InlineData("1password", "get_item", "_1password__get_item")]
    [InlineData("-tools", "x", "_-tools__x")]
    [InlineData("files", "read", "files__read")]
    public void Api_names_start_with_a_letter_or_underscore(string server, string tool, string expected)
        => Assert.Equal(expected, ToolNames.ForApi(server, tool, new HashSet<string>()));

    [Fact]
    public void Saving_an_approval_backs_up_the_hand_written_file_first()
    {
        const string original = "{\n  // my servers\n  \"mcpServers\": {}\n}";
        WriteConfig(original);
        McpConfig.SaveApproval(McpPath, "files/write", ToolApproval.Allow);
        var backup = Assert.Single(Directory.GetFiles(_dir, "mcp.json.bak-*"));
        Assert.Equal(original, File.ReadAllText(backup));
        Assert.Equal(ToolApproval.Allow, McpConfig.Load(McpPath).Approvals["files/write"]);
    }

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
        var offered = await host.GetToolsAsync(default);
        var broken = await ToolLoop.CallAsync(host, offered, "files__read_file", "{\"path\":", default);
        var notObject = await ToolLoop.CallAsync(host, offered, "files__read_file", "[1]", default);
        Assert.True(broken.IsError && notObject.IsError);
        Assert.Empty(host.Calls);
        await ToolLoop.CallAsync(host, offered, "files__read_file", "", default); // empty = no arguments
        Assert.Single(host.Calls);
        Assert.Equal("(no output)", ToolLoop.ResultText(new ToolResult("", false)));
    }

    [Fact]
    public async Task Names_the_model_was_not_offered_go_nowhere()
    {
        var host = new ToolLoopTests.FakeToolHost();
        var result = await ToolLoop.CallAsync(host, [], "files__read_file", "{}", default);
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
    public void Unknown_approval_values_count_as_deny_with_a_warning()
    {
        var config = McpConfig.Parse("""{ "approvals": { "a/x": "3", "a/y": "allow, deny", "a/z": "denied", "a/ok": "ALLOW" } }""");
        Assert.Equal(ToolApproval.Deny, config.Approvals["a/x"]);
        Assert.Equal(ToolApproval.Deny, config.Approvals["a/y"]);
        Assert.Equal(ToolApproval.Deny, config.Approvals["a/z"]);
        Assert.Equal(ToolApproval.Allow, config.Approvals["a/ok"]);
        Assert.Equal(3, config.Warnings.Count);
    }

    [Fact]
    public async Task An_unreadable_mcp_json_keeps_the_last_good_rules_and_servers()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" } }, "approvals": { "a/secret": "deny" } }""");
        var session = new FakeSession(("open", true), ("secret", true));
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [], (_, _) => Task.FromResult<IMcpSession>(session),
            (_, _) => Task.FromResult(ToolDecision.Deny), Log);
        Assert.Equal(["open"], (await host.GetToolsAsync(default)).Select(t => t.Tool));

        File.WriteAllText(McpPath, "{ half-saved");
        Assert.Equal(["open"], (await host.GetToolsAsync(default)).Select(t => t.Tool)); // the deny still applies
        Assert.False(session.Disposed);                                                  // and the server kept running
        Assert.Contains("last good", host.ConfigError);
    }

    [Fact]
    public async Task With_no_good_mcp_json_yet_no_tools_are_offered()
    {
        WriteConfig("{ broken");
        var connects = 0;
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [new McpServerConfig("windows-x", "odr", [], new Dictionary<string, string>())],
            (_, _) => { connects++; return Task.FromResult<IMcpSession>(new FakeSession(("t", true))); }, (_, _) => Task.FromResult(ToolDecision.Deny), Log);
        Assert.Empty(await host.GetToolsAsync(default));
        Assert.Equal(0, connects);
    }

    [Fact]
    public async Task Tools_and_servers_with_slash_or_star_names_are_skipped()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" }, "b/c": { "command": "y" } } }""");
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (_, _) => Task.FromResult<IMcpSession>(new FakeSession(("*", true), ("x/y", true), ("fine", true))), (_, _) => Task.FromResult(ToolDecision.Deny), Log);
        var tools = await host.GetToolsAsync(default);
        Assert.Equal([("a", "fine")], tools.Select(t => (t.Server, t.Tool)));
        Assert.Contains(host.Status, s => s.Name == "b/c" && s.State == McpServerState.Failed);
    }

    [Fact]
    public async Task A_dead_server_is_restarted_on_the_next_answer()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" } } }""");
        var sessions = new List<FakeSession>();
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (_, _) => { var s = new FakeSession(("read", true)) { Throw = sessions.Count == 0 }; sessions.Add(s); return Task.FromResult<IMcpSession>(s); },
            (_, _) => Task.FromResult(ToolDecision.Deny), Log);
        var tools = await host.GetToolsAsync(default);
        Assert.True((await host.CallAsync(tools[0], JsonDocument.Parse("{}").RootElement, default)).IsError);
        tools = await host.GetToolsAsync(default);
        Assert.Equal(2, sessions.Count);
        Assert.True(sessions[0].Disposed);
        Assert.False((await host.CallAsync(tools[0], JsonDocument.Parse("{}").RootElement, default)).IsError);
    }

    [Fact]
    public async Task After_dispose_no_servers_start_again()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" } } }""");
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [], (_, _) => Task.FromResult<IMcpSession>(new FakeSession(("t", true))),
            (_, _) => Task.FromResult(ToolDecision.Deny), Log);
        await host.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.GetToolsAsync(default));
    }

    [Fact]
    public void Two_saves_in_the_same_second_keep_the_original_backup_and_both_rules()
    {
        const string original = "{\n  // mine\n  \"mcpServers\": {}\n}";
        WriteConfig(original);
        McpConfig.SaveApproval(McpPath, "a/x", ToolApproval.Allow);
        File.AppendAllText(McpPath, "\n// added later\n");
        McpConfig.SaveApproval(McpPath, "a/y", ToolApproval.Allow);
        var backups = Directory.GetFiles(_dir, "mcp.json.bak-*").Select(File.ReadAllText).ToList();
        Assert.Contains(original, backups);
        Assert.Equal(2, backups.Count);
        var approvals = McpConfig.Load(McpPath).Approvals;
        Assert.True(approvals.ContainsKey("a/x") && approvals.ContainsKey("a/y"));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task A_tool_that_stopped_being_read_only_asks_even_with_an_old_spec()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" } } }""");
        var readOnly = true;
        var asked = 0;
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (_, _) => Task.FromResult<IMcpSession>(new FakeSession(("t", true)) { ReadOnlyNow = () => readOnly }),
            (_, _) => { asked++; return Task.FromResult(ToolDecision.Deny); }, Log);
        var old = (await host.GetToolsAsync(default))[0];
        readOnly = false;
        File.WriteAllText(McpPath, """{ "mcpServers": { "a": { "command": "x", "args": ["v2"] } } }"""); // restart → fresh list
        await host.GetToolsAsync(default);
        var result = await host.CallAsync(old, JsonDocument.Parse("{}").RootElement, default);
        Assert.Equal(1, asked);
        Assert.True(result.IsError);
    }

    [Fact]
    public void An_approval_is_not_saved_over_a_file_that_is_not_an_object()
    {
        WriteConfig("""[ { "mcpServers": {} } ]""");
        Assert.Throws<JsonException>(() => McpConfig.SaveApproval(McpPath, "a/x", ToolApproval.Allow));
        Assert.Equal("""[ { "mcpServers": {} } ]""", File.ReadAllText(McpPath));
    }
}
