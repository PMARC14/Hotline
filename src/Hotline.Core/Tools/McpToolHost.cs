using System.Text.Json;
using Hotline.Core.Diagnostics;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Hotline.Core.Tools;

/// <summary>A tool offered to an API model. ApiName is what the model calls; Server/Tool identify the MCP tool.</summary>
public sealed record ToolSpec(string ApiName, string Server, string Tool, string Description, JsonElement InputSchema, bool ReadOnly);

public sealed record ToolResult(string Text, bool IsError);

public sealed record ToolCallRequest(ToolSpec Tool, JsonElement Arguments);

public enum ToolDecision { AllowOnce, AllowAlways, Deny }

/// <summary>What the API backends see: the tools on offer and a way to call one (approval is handled inside).</summary>
public interface IToolHost
{
    Task<IReadOnlyList<ToolSpec>> GetToolsAsync(CancellationToken ct);
    Task<ToolResult> CallAsync(string apiName, JsonElement arguments, CancellationToken ct);
}

public sealed record McpToolInfo(string Name, string Description, JsonElement InputSchema, bool ReadOnly);

/// <summary>One running MCP server connection.</summary>
public interface IMcpSession : IAsyncDisposable
{
    Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct);
    Task<ToolResult> CallAsync(string tool, JsonElement arguments, CancellationToken ct);
}

public enum McpServerState { NotStarted, Starting, Ready, Failed, Disabled }

public sealed record McpServerStatus(string Name, string DisplayName, McpServerState State, int ToolCount, string? Error);

/// <summary>
/// Hosts the MCP servers from mcp.json (+ the Windows agent registry): starts them lazily on first use, keeps going if
/// one fails, applies the approval policy before every call ("ask" goes to the panel; "Always" is saved to mcp.json).
/// </summary>
public sealed class McpToolHost(
    Func<McpConfig> loadConfig,
    Func<IReadOnlyList<McpServerConfig>> extraServers,
    Func<McpServerConfig, CancellationToken, Task<IMcpSession>> connect,
    Func<ToolCallRequest, CancellationToken, Task<ToolDecision>> approve,
    FileLog log,
    string? configPath = null) : IToolHost, IAsyncDisposable
{
    private const int MaxResultChars = 20_000;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, IMcpSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, McpServerStatus> _status = new(StringComparer.OrdinalIgnoreCase);
    private List<ToolSpec> _tools = [];
    private McpConfig _config = new();
    private string? _configFingerprint;

    public IReadOnlyList<McpServerStatus> Status { get { lock (_status) return _status.Values.ToList(); } }
    public string? ConfigError => _config.Error;
    public event Action? StatusChanged;

    public async Task<IReadOnlyList<ToolSpec>> GetToolsAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var config = loadConfig();
            var servers = config.Servers.Concat(extraServers()).ToList();
            var fingerprint = JsonSerializer.Serialize(servers.Select(s => new { s.Name, s.Command, s.Args, s.Env, s.Disabled, s.WorkingDirectory }));
            _config = config;
            if (fingerprint != _configFingerprint)
            {
                await StopAllAsync(); // server list changed: restart from the new config
                _configFingerprint = fingerprint;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tools = new List<ToolSpec>();
            foreach (var server in servers)
            {
                var display = server.DisplayName ?? server.Name;
                if (server.Disabled) { SetStatus(new(server.Name, display, McpServerState.Disabled, 0, null)); continue; }
                try
                {
                    if (!_sessions.TryGetValue(server.Name, out var session))
                    {
                        SetStatus(new(server.Name, display, McpServerState.Starting, 0, null));
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(30));
                        session = await connect(server, timeout.Token);
                        _sessions[server.Name] = session;
                    }
                    var list = await session.ListToolsAsync(ct);
                    var offered = 0;
                    foreach (var t in list)
                    {
                        if (ToolPolicy.Decide(config.Approvals, server.Name, t.Name, t.ReadOnly) == ToolApproval.Deny) continue; // never offered
                        tools.Add(new ToolSpec(ToolNames.ForApi(server.Name, t.Name, used), server.Name, t.Name, t.Description, t.InputSchema, t.ReadOnly));
                        offered++;
                    }
                    SetStatus(new(server.Name, display, McpServerState.Ready, offered, null));
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    log.Error($"MCP server {server.Name} failed", ex);
                    if (_sessions.Remove(server.Name, out var dead)) await SafeDispose(dead);
                    SetStatus(new(server.Name, display, McpServerState.Failed, 0, ex.Message));
                }
            }
            _tools = tools;
            return tools;
        }
        finally { _gate.Release(); }
    }

    public async Task<ToolResult> CallAsync(string apiName, JsonElement arguments, CancellationToken ct)
    {
        var spec = _tools.FirstOrDefault(t => t.ApiName == apiName);
        if (spec is null) return new ToolResult($"There is no tool named {apiName}.", true);
        var decision = ToolPolicy.Decide(_config.Approvals, spec.Server, spec.Tool, spec.ReadOnly);
        if (decision == ToolApproval.Deny) return new ToolResult($"The user doesn't allow {spec.Server}/{spec.Tool}.", true);
        if (decision == ToolApproval.Ask)
        {
            var answer = await approve(new ToolCallRequest(spec, arguments), ct);
            if (answer == ToolDecision.Deny) return new ToolResult($"The user declined running {spec.Server}/{spec.Tool}.", true);
            if (answer == ToolDecision.AllowAlways) Remember(spec);
        }
        if (!_sessions.TryGetValue(spec.Server, out var session)) return new ToolResult($"The {spec.Server} tools aren't running.", true);
        try
        {
            var result = await session.CallAsync(spec.Tool, arguments, ct);
            log.Info($"tool {spec.Server}/{spec.Tool}: {(result.IsError ? "error" : "ok")} ({result.Text.Length} chars)");
            return result.Text.Length <= MaxResultChars ? result
                : result with { Text = result.Text[..MaxResultChars] + $"\n…(truncated, {result.Text.Length} chars total)" };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error($"tool {spec.Server}/{spec.Tool} failed", ex);
            return new ToolResult($"The tool failed: {ex.Message}", true);
        }
    }

    private void Remember(ToolSpec spec)
    {
        var key = $"{spec.Server}/{spec.Tool}";
        var approvals = new Dictionary<string, ToolApproval>(_config.Approvals, StringComparer.OrdinalIgnoreCase) { [key] = ToolApproval.Allow };
        _config = new McpConfig { Servers = _config.Servers, Approvals = approvals };
        if (configPath is null) return;
        try { McpConfig.SaveApproval(configPath, key, ToolApproval.Allow); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { log.Error("saving the tool approval failed", ex); }
    }

    private void SetStatus(McpServerStatus status)
    {
        lock (_status) _status[status.Name] = status;
        StatusChanged?.Invoke();
    }

    private async Task StopAllAsync()
    {
        foreach (var session in _sessions.Values) await SafeDispose(session);
        _sessions.Clear();
        lock (_status) _status.Clear();
    }

    private static async Task SafeDispose(IMcpSession session)
    {
        try { await session.DisposeAsync(); } catch (Exception) { /* already gone */ }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { await StopAllAsync(); }
        finally { _gate.Release(); }
    }
}

/// <summary>A real MCP server over stdio through the official ModelContextProtocol SDK.</summary>
public sealed class StdioMcpSession : IMcpSession
{
    private readonly McpClient _client;
    private StdioMcpSession(McpClient client) => _client = client;

    public static async Task<IMcpSession> ConnectAsync(McpServerConfig server, CancellationToken ct)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = server.Name,
            Command = server.Command,
            Arguments = server.Args.ToList(),
            WorkingDirectory = server.WorkingDirectory,
            EnvironmentVariables = server.Env.ToDictionary(kv => kv.Key, kv => (string?)kv.Value),
        });
        var client = await McpClient.CreateAsync(transport, new McpClientOptions { ClientInfo = new Implementation { Name = "Hotline", Version = "1.0" } }, cancellationToken: ct);
        return new StdioMcpSession(client);
    }

    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct)
    {
        var tools = await _client.ListToolsAsync(cancellationToken: ct);
        return tools.Select(t => new McpToolInfo(t.Name, t.Description ?? "", t.JsonSchema, t.ProtocolTool.Annotations?.ReadOnlyHint == true)).ToList();
    }

    public async Task<ToolResult> CallAsync(string tool, JsonElement arguments, CancellationToken ct)
    {
        var args = arguments.ValueKind == JsonValueKind.Object
            ? arguments.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone())
            : new Dictionary<string, object?>();
        var result = await _client.CallToolAsync(tool, args, cancellationToken: ct);
        var text = string.Join("\n", result.Content.Select(c => c switch
        {
            TextContentBlock t => t.Text,
            _ => $"[{c.Type} content]",
        }));
        if (text.Length == 0 && result.StructuredContent is { } structured) text = structured.ToString();
        return new ToolResult(text, result.IsError == true);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
