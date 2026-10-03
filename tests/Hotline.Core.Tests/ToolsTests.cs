using System.Text.Json;
using Hotline.Core.Diagnostics;
using Hotline.Core.Tools;

namespace Hotline.Core.Tests;

public sealed class ToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
    private string McpPath => Path.Combine(_dir, "mcp.json");

    // ---- mcp.json ------------------------------------------------------------------------------

    [Fact]
    public void Parses_the_common_mcpServers_format_and_approvals()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(McpPath, """
            {
              // pasted from another app
              "mcpServers": {
                "files": { "command": "npx", "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Notes"], "env": { "X": "1" } },
                "off": { "command": "x.exe", "disabled": true }
              },
              "approvals": { "files/write_file": "deny", "files/*": "allow" },
            }
            """);
        var config = McpConfig.Load(McpPath);
        var files = config.Servers.Single(s => s.Name == "files");
        Assert.Equal("npx", files.Command);
        Assert.Equal(["-y", "@modelcontextprotocol/server-filesystem", @"C:\Notes"], files.Args);
        Assert.Equal("1", files.Env["X"]);
        Assert.True(config.Servers.Single(s => s.Name == "off").Disabled);
        Assert.Equal(ToolApproval.Deny, config.Approvals["files/write_file"]);
    }

    [Fact]
    public void Missing_file_is_created_with_an_empty_example_and_broken_file_is_reported_not_overwritten()
    {
        var config = McpConfig.Load(McpPath);
        Assert.Empty(config.Servers);
        Assert.True(File.Exists(McpPath));
        Assert.Contains("mcpServers", File.ReadAllText(McpPath));

        File.WriteAllText(McpPath, "{ broken");
        var broken = McpConfig.Load(McpPath);
        Assert.NotNull(broken.Error);
        Assert.Equal("{ broken", File.ReadAllText(McpPath));
    }

    [Fact]
    public void Saving_an_approval_keeps_everything_else_in_the_file()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(McpPath, """{ "mcpServers": { "files": { "command": "npx", "custom": 5 } } }""");
        McpConfig.SaveApproval(McpPath, "files/write_file", ToolApproval.Allow);
        var text = File.ReadAllText(McpPath);
        Assert.Contains("\"custom\": 5", text);
        Assert.Equal(ToolApproval.Allow, McpConfig.Load(McpPath).Approvals["files/write_file"]);
    }

    // ---- policy --------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, null, ToolApproval.Allow)]      // read-only: no prompt
    [InlineData(false, null, ToolApproval.Ask)]       // may change things: ask
    [InlineData(true, "deny", ToolApproval.Deny)]     // explicit deny wins
    [InlineData(false, "allow", ToolApproval.Allow)]
    public void Policy(bool readOnly, string? rule, ToolApproval expected)
    {
        var approvals = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase);
        if (rule is not null) approvals["files/tool"] = Enum.Parse<ToolApproval>(rule, ignoreCase: true);
        Assert.Equal(expected, ToolPolicy.Decide(approvals, "files", "tool", readOnly));
    }

    [Fact]
    public void Server_wildcards_apply_and_specific_rules_win()
    {
        var approvals = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase)
        {
            ["files/*"] = ToolApproval.Allow, ["files/delete"] = ToolApproval.Deny,
        };
        Assert.Equal(ToolApproval.Allow, ToolPolicy.Decide(approvals, "files", "write", readOnly: false));
        Assert.Equal(ToolApproval.Deny, ToolPolicy.Decide(approvals, "files", "delete", readOnly: false));
    }

    // ---- Windows on-device agent registry ------------------------------------------------------

    [Fact]
    public void Odr_list_becomes_windows_servers()
    {
        const string json = """
            [
              { "id": "FilesAgentMcpServer", "manifest": { "name": "file-mcp-server", "display_name": "File Explorer",
                  "server": { "mcp_config": { "command": "odr.exe", "args": ["mcp", "run", "--id", "FilesAgentMcpServer"] } } } },
              { "id": "broken" }
            ]
            """;
        var servers = OdrDiscovery.Parse(json);
        var files = Assert.Single(servers);
        Assert.Equal("windows-FilesAgentMcpServer", files.Name);
        Assert.Equal("odr.exe", files.Command);
        Assert.Equal(["mcp", "run", "--id", "FilesAgentMcpServer"], files.Args);
        Assert.Equal("File Explorer", files.DisplayName);
        Assert.Empty(OdrDiscovery.Parse("not json"));
    }

    // ---- tool names for the APIs ---------------------------------------------------------------

    [Fact]
    public void Api_tool_names_are_safe_unique_and_short()
    {
        var names = new HashSet<string>();
        var a = ToolNames.ForApi("windows-files", "search files!", names);
        var b = ToolNames.ForApi("windows-files", "search files?", names);
        Assert.Matches("^[A-Za-z0-9_-]{1,64}$", a);
        Assert.NotEqual(a, b);
        Assert.True(ToolNames.ForApi(new string('s', 80), new string('t', 80), names).Length <= 64);
    }

    // ---- tool host -----------------------------------------------------------------------------

    private sealed class FakeSession(params (string Name, bool ReadOnly)[] tools) : IMcpSession
    {
        public List<string> Called { get; } = [];
        public Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<McpToolInfo>>(tools.Select(t => new McpToolInfo(t.Name, $"does {t.Name}", JsonDocument.Parse("""{"type":"object"}""").RootElement, t.ReadOnly)).ToList());
        public Task<ToolResult> CallAsync(string tool, JsonElement args, CancellationToken ct)
        {
            Called.Add(tool);
            return Task.FromResult(new ToolResult($"{tool} done", false));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private McpToolHost Host(FakeSession session, Func<ToolCallRequest, CancellationToken, Task<ToolDecision>> approve, string mcpJson)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(McpPath, mcpJson);
        return new McpToolHost(() => McpConfig.Load(McpPath), () => [], (_, _) => Task.FromResult<IMcpSession>(session), approve, new FileLog(Path.Combine(_dir, "h.log")), McpPath);
    }

    private const string OneServer = """{ "mcpServers": { "files": { "command": "x" } } }""";

    [Fact]
    public async Task Read_only_tools_run_without_asking()
    {
        var session = new FakeSession(("read", true));
        var asked = 0;
        var host = Host(session, (_, _) => { asked++; return Task.FromResult(ToolDecision.Deny); }, OneServer);
        var tools = await host.GetToolsAsync(default);
        var result = await host.CallAsync(tools[0].ApiName, JsonDocument.Parse("{}").RootElement, default);
        Assert.Equal(("read done", false, 0), (result.Text, result.IsError, asked));
    }

    [Fact]
    public async Task Other_tools_ask_and_deny_is_an_error_result_not_a_call()
    {
        var session = new FakeSession(("write", false));
        var host = Host(session, (_, _) => Task.FromResult(ToolDecision.Deny), OneServer);
        var tools = await host.GetToolsAsync(default);
        var result = await host.CallAsync(tools[0].ApiName, JsonDocument.Parse("{}").RootElement, default);
        Assert.True(result.IsError);
        Assert.Contains("declined", result.Text);
        Assert.Empty(session.Called);
    }

    [Fact]
    public async Task Allow_always_is_remembered_in_mcp_json()
    {
        var session = new FakeSession(("write", false));
        var asked = 0;
        var host = Host(session, (_, _) => { asked++; return Task.FromResult(ToolDecision.AllowAlways); }, OneServer);
        var tools = await host.GetToolsAsync(default);
        await host.CallAsync(tools[0].ApiName, JsonDocument.Parse("{}").RootElement, default);
        await host.CallAsync(tools[0].ApiName, JsonDocument.Parse("{}").RootElement, default);
        Assert.Equal(1, asked);
        Assert.Equal(ToolApproval.Allow, McpConfig.Load(McpPath).Approvals["files/write"]);
    }

    [Fact]
    public async Task Denied_tools_are_not_offered_and_a_failing_server_does_not_break_others()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(McpPath, """{ "mcpServers": { "good": { "command": "x" }, "bad": { "command": "y" } }, "approvals": { "good/secret": "deny" } }""");
        var good = new FakeSession(("read", true), ("secret", true));
        var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [],
            (server, _) => server.Name == "bad" ? throw new InvalidOperationException("won't start") : Task.FromResult<IMcpSession>(good),
            (_, _) => Task.FromResult(ToolDecision.Deny), new FileLog(Path.Combine(_dir, "h.log")));
        var tools = await host.GetToolsAsync(default);
        Assert.Equal(["read"], tools.Select(t => t.Tool));
        Assert.Contains(host.Status, s => s.Name == "bad" && s.State == McpServerState.Failed && s.Error!.Contains("won't start"));
    }

    [Fact]
    public async Task Unknown_tool_name_is_an_error_result()
    {
        var host = Host(new FakeSession(("read", true)), (_, _) => Task.FromResult(ToolDecision.Deny), OneServer);
        await host.GetToolsAsync(default);
        Assert.True((await host.CallAsync("nope", JsonDocument.Parse("{}").RootElement, default)).IsError);
    }
}
