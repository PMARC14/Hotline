using System.Text.Json;
using Hotline.Core.Diagnostics;
using Hotline.Core.Tools;

namespace Hotline.Core.Tests;

public sealed class ToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
    private string McpPath => Path.Combine(_dir, "mcp.json");

    private void WriteMcp(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(McpPath, json);
    }

    // ---- mcp.json ------------------------------------------------------------------------------

    [Fact]
    public void Parses_the_common_mcpServers_format_and_approvals()
    {
        WriteMcp("""
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
        WriteMcp("""{ "mcpServers": { "files": { "command": "npx", "custom": 5 } } }""");
        McpConfig.SaveApproval(McpPath, "files/write_file", ToolApproval.Allow);
        var text = File.ReadAllText(McpPath);
        Assert.Contains("\"custom\": 5", text);
        Assert.Equal(ToolApproval.Allow, McpConfig.Load(McpPath).Approvals["files/write_file"]);
    }

    [Fact]
    public void Env_keys_differing_only_in_case_take_the_last_value()
    {
        var config = McpConfig.Parse("""{ "mcpServers": { "a": { "command": "x.exe", "env": { "PATH": "C:\\one", "Path": "C:\\two" } } } }""");
        Assert.Equal(@"C:\two", Assert.Single(config.Servers).Env["PATH"]);
    }

    [Theory]
    [InlineData("""{ "mcpServers": { "a": { "command": "x.exe" }, "a": { "command": "y.exe" } } }""")]
    [InlineData("""{ "approvals": { "a/x": "allow", "a/x": "deny" } }""")]
    [InlineData("""{ "mcpServers": { "a": { "command": "x.exe", "env": { "K": "1", "K": "2" } } } }""")]
    public void Duplicate_keys_make_the_file_unreadable_not_a_crash(string json)
    {
        WriteMcp(json);
        var config = McpConfig.Load(McpPath);
        Assert.NotNull(config.Error);
        Assert.Empty(config.Servers);
        Assert.Equal(json, File.ReadAllText(McpPath)); // left as it is
        Assert.Throws<JsonException>(() => McpConfig.SaveApproval(McpPath, "a/y", ToolApproval.Allow)); // the caller logs a JsonException
        Assert.Equal(json, File.ReadAllText(McpPath));
    }

    [Fact]
    public void Config_with_a_non_string_command_cwd_or_name_skips_only_that_part()
    {
        var config = McpConfig.Parse("""
            {
              "mcpServers": {
                "bad": { "command": true },
                "num": { "command": "tool", "cwd": 123, "displayName": [1] },
                "ok": { "command": "tool2", "cwd": "C:\\work", "displayName": "Okay" }
              }
            }
            """);
        Assert.Null(config.Error);
        Assert.Equal(["num", "ok"], config.Servers.Select(s => s.Name));
        Assert.Null(config.Servers[0].WorkingDirectory);
        Assert.Null(config.Servers[0].DisplayName);
        Assert.Equal("Okay", config.Servers[1].DisplayName);
    }

    [Fact]
    public void Saving_an_approval_creates_the_folder_when_it_is_missing()
    {
        var path = Path.Combine(_dir, "not-yet", "mcp.json");
        McpConfig.SaveApproval(path, "files/read", ToolApproval.Allow);
        Assert.Equal(ToolApproval.Allow, McpConfig.Load(path).Approvals["files/read"]);
    }

    [Fact]
    public void Saving_an_approval_backs_up_any_file_it_overwrites()
    {
        const string original = """{ "mcpServers": {} }""";
        WriteMcp(original);
        McpConfig.SaveApproval(McpPath, "a/x", ToolApproval.Allow);
        Assert.Equal(original, File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "mcp.json.bak-*"))));
        // Nothing to change: nothing written, no new backup.
        McpConfig.SaveApproval(McpPath, "a/x", ToolApproval.Allow);
        Assert.Single(Directory.GetFiles(_dir, "mcp.json.bak-*"));
    }

    // ---- policy --------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, "tool", null, ToolApproval.Allow)]      // read-only: no prompt
    [InlineData(false, "tool", null, ToolApproval.Ask)]       // may change things: ask
    [InlineData(true, "tool", "files/tool=deny", ToolApproval.Deny)] // explicit deny wins
    [InlineData(false, "tool", "files/tool=allow", ToolApproval.Allow)]
    [InlineData(false, "write", "files/*=allow,files/delete=deny", ToolApproval.Allow)]  // server wildcard applies...
    [InlineData(false, "delete", "files/*=allow,files/delete=deny", ToolApproval.Deny)]  // ...and the specific rule wins
    public void Policy(bool readOnly, string tool, string? rules, ToolApproval expected)
    {
        var approvals = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules?.Split(',') ?? [])
        {
            var parts = rule.Split('=');
            approvals[parts[0]] = Enum.Parse<ToolApproval>(parts[1], ignoreCase: true);
        }
        Assert.Equal(expected, ToolPolicy.Decide(approvals, "files", tool, readOnly));
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
        WriteMcp(mcpJson);
        return new McpToolHost(() => McpConfig.Load(McpPath), () => [], (_, _) => Task.FromResult<IMcpSession>(session), approve, new FileLog(Path.Combine(_dir, "h.log")), McpPath);
    }

    private const string OneServer = """{ "mcpServers": { "files": { "command": "x" } } }""";

    [Theory]
    [InlineData(true, 0, false, "tool done", 1)]  // read-only tools run without asking
    [InlineData(false, 1, true, "declined", 0)]   // others ask; deny is an error result, not a call
    public async Task Tools_ask_unless_read_only_and_deny_is_an_error_result_not_a_call(bool readOnly, int expectedAsks, bool expectError, string expectedText, int expectedCalls)
    {
        var session = new FakeSession(("tool", readOnly));
        var asked = 0;
        var host = Host(session, (_, _) => { asked++; return Task.FromResult(ToolDecision.Deny); }, OneServer);
        var tools = await host.GetToolsAsync(default);
        var result = await host.CallAsync(tools[0], JsonDocument.Parse("{}").RootElement, default);
        Assert.Equal(expectError, result.IsError);
        Assert.Contains(expectedText, result.Text);
        Assert.Equal(expectedAsks, asked);
        Assert.Equal(expectedCalls, session.Called.Count);
    }

    [Fact]
    public async Task Allow_always_is_remembered_in_mcp_json()
    {
        var session = new FakeSession(("write", false));
        var asked = 0;
        var host = Host(session, (_, _) => { asked++; return Task.FromResult(ToolDecision.AllowAlways); }, OneServer);
        var tools = await host.GetToolsAsync(default);
        await host.CallAsync(tools[0], JsonDocument.Parse("{}").RootElement, default);
        await host.CallAsync(tools[0], JsonDocument.Parse("{}").RootElement, default);
        Assert.Equal(1, asked);
        Assert.Equal(ToolApproval.Allow, McpConfig.Load(McpPath).Approvals["files/write"]);
    }

    [Fact]
    public async Task Denied_tools_are_not_offered_and_a_failing_server_does_not_break_others()
    {
        WriteMcp("""{ "mcpServers": { "good": { "command": "x" }, "bad": { "command": "y" } }, "approvals": { "good/secret": "deny" } }""");
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
        var stranger = new ToolSpec("x__nope", "x", "nope", "", JsonDocument.Parse("{}").RootElement, true);
        Assert.True((await host.CallAsync(stranger, JsonDocument.Parse("{}").RootElement, default)).IsError);
    }
}
