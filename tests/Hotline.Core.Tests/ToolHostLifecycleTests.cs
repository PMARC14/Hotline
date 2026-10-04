using System.Text.Json;
using Hotline.Core.Backends.Api;
using Hotline.Core.Diagnostics;
using Hotline.Core.Tools;

namespace Hotline.Core.Tests;

/// <summary>McpToolHost lifetime: cached tool lists, per-server restarts, start timeouts, failure backoff; tool helpers.</summary>
public sealed class ToolHostLifecycleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
    private string McpPath => Path.Combine(_dir, "mcp.json");
    private FileLog Log => new(Path.Combine(_dir, "h.log"));

    private sealed class CountingSession(string tool, TimeSpan listDelay = default) : IMcpSession
    {
        public int Lists;
        public bool Disposed;
        public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct)
        {
            Lists++;
            if (listDelay > TimeSpan.Zero) await Task.Delay(listDelay, ct);
            return [new McpToolInfo(tool, "", JsonDocument.Parse("""{"type":"object"}""").RootElement, true)];
        }
        public Task<ToolResult> CallAsync(string t, JsonElement args, CancellationToken ct) => Task.FromResult(new ToolResult("ok", false));
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private void WriteConfig(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(McpPath, json);
    }

    [Fact]
    public async Task Tool_lists_are_cached_and_only_a_changed_server_restarts()
    {
        WriteConfig("""{ "mcpServers": { "a": { "command": "x" }, "b": { "command": "y" } } }""");
        var sessions = new List<(string Server, CountingSession Session)>();
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (server, _) => { var s = new CountingSession(server.Name + "_tool"); sessions.Add((server.Name, s)); return Task.FromResult<IMcpSession>(s); },
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
        var sessions = new List<CountingSession>();
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => extra,
            (server, _) => { var s = new CountingSession(server.Name); sessions.Add(s); return Task.FromResult<IMcpSession>(s); },
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
        var slow = new CountingSession("s", TimeSpan.FromMinutes(5));
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (server, _) => Task.FromResult<IMcpSession>(server.Name == "slow" ? slow : new CountingSession("fast")),
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

    [Fact]
    public void Api_names_start_with_a_letter_or_underscore()
    {
        var used = new HashSet<string>();
        Assert.Equal("_1password__get_item", ToolNames.ForApi("1password", "get_item", used));
        Assert.Equal("_-tools__x", ToolNames.ForApi("-tools", "x", used));
        Assert.Equal("files__read", ToolNames.ForApi("files", "read", used));
    }

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
        var broken = await ToolLoop.CallAsync(host, "files__read_file", "{\"path\":", default);
        var notObject = await ToolLoop.CallAsync(host, "files__read_file", "[1]", default);
        Assert.True(broken.IsError && notObject.IsError);
        Assert.Empty(host.Calls);
        await ToolLoop.CallAsync(host, "files__read_file", "", default); // empty = no arguments
        Assert.Single(host.Calls);
        Assert.Equal("(no output)", ToolLoop.ResultText(new ToolResult("", false)));
    }
}
