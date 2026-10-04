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
/// Hosts the MCP servers from mcp.json (+ the Windows agent registry): starts them lazily on first use (in parallel),
/// keeps going if one fails, applies the approval policy before every call ("ask" goes to the panel; "Always" is saved
/// to mcp.json). Each server's tool list is cached while it runs; a server restarts only when its own entry changes, and
/// one that failed to start is retried after <see cref="RetryFailedAfter"/> (or on <see cref="RetryFailedServers"/>).
/// </summary>
public sealed class McpToolHost(
    Func<McpConfig> loadConfig,
    Func<IReadOnlyList<McpServerConfig>> extraServers,
    Func<McpServerConfig, CancellationToken, Task<IMcpSession>> connect,
    Func<ToolCallRequest, CancellationToken, Task<ToolDecision>> approve,
    FileLog log,
    string? configPath = null,
    TimeProvider? time = null) : IToolHost, IAsyncDisposable
{
    private const int MaxResultChars = 20_000;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Running> _running = new(StringComparer.OrdinalIgnoreCase); // under _gate
    private readonly Dictionary<string, (string Fingerprint, DateTimeOffset At)> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, McpServerStatus> _status = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _snapshotLock = new();
    private Snapshot _snapshot = new([], new McpConfig(), new Dictionary<string, IMcpSession>());

    /// <summary>Connecting plus listing a server's tools must finish within this.</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryFailedAfter { get; init; } = TimeSpan.FromMinutes(1);

    private sealed record Running(IMcpSession Session, string Fingerprint, IReadOnlyList<McpToolInfo> Tools);

    /// <summary>What calls use: replaced as a whole, so a call never sees half of an update.</summary>
    private sealed record Snapshot(IReadOnlyList<ToolSpec> Tools, McpConfig Config, IReadOnlyDictionary<string, IMcpSession> Sessions);

    public IReadOnlyList<McpServerStatus> Status { get { lock (_status) return _status.Values.ToList(); } }
    public string? ConfigError => Volatile.Read(ref _snapshot).Config.Error;
    public event Action? StatusChanged;

    /// <summary>Forget start failures so the next <see cref="GetToolsAsync"/> tries those servers again ("Check servers").</summary>
    public void RetryFailedServers() { lock (_failed) _failed.Clear(); }

    public async Task<IReadOnlyList<ToolSpec>> GetToolsAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var config = loadConfig();
            var servers = config.Servers.Concat(extraServers()).DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var fingerprints = servers.ToDictionary(s => s.Name, Fingerprint, StringComparer.OrdinalIgnoreCase);

            // Stop only servers that were removed, disabled or changed.
            foreach (var (name, running) in _running.ToList())
                if (!fingerprints.TryGetValue(name, out var fp) || fp != running.Fingerprint || servers.First(s => Same(s.Name, name)).Disabled)
                {
                    _running.Remove(name);
                    await SafeDispose(running.Session);
                }
            lock (_status)
                foreach (var name in _status.Keys.Where(k => !fingerprints.ContainsKey(k)).ToList()) _status.Remove(name);

            var now = _time.GetUtcNow();
            var toStart = servers.Where(s => !s.Disabled && !_running.ContainsKey(s.Name) && !RecentlyFailed(s.Name, fingerprints[s.Name], now)).ToList();
            foreach (var server in toStart) SetStatus(new(server.Name, server.DisplayName ?? server.Name, McpServerState.Starting, 0, null));
            var started = await Task.WhenAll(toStart.Select(server => StartAsync(server, fingerprints[server.Name], ct)));
            foreach (var (server, running, error) in started)
            {
                if (running is not null)
                {
                    _running[server.Name] = running;
                    lock (_failed) _failed.Remove(server.Name);
                }
                else if (!ct.IsCancellationRequested)
                {
                    lock (_failed) _failed[server.Name] = (fingerprints[server.Name], now);
                    SetStatus(new(server.Name, server.DisplayName ?? server.Name, McpServerState.Failed, 0, error));
                }
            }
            ct.ThrowIfCancellationRequested();

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tools = new List<ToolSpec>();
            foreach (var server in servers) // config order, so tool names stay stable between answers
            {
                var display = server.DisplayName ?? server.Name;
                if (server.Disabled) { SetStatus(new(server.Name, display, McpServerState.Disabled, 0, null)); continue; }
                if (!_running.TryGetValue(server.Name, out var running)) continue; // failed (status says why)
                var offered = 0;
                foreach (var t in running.Tools)
                {
                    if (ToolPolicy.Decide(config.Approvals, server.Name, t.Name, t.ReadOnly) == ToolApproval.Deny) continue; // never offered
                    tools.Add(new ToolSpec(ToolNames.ForApi(server.Name, t.Name, used), server.Name, t.Name, t.Description, t.InputSchema, t.ReadOnly));
                    offered++;
                }
                SetStatus(new(server.Name, display, McpServerState.Ready, offered, null));
            }
            var sessions = _running.ToDictionary(kv => kv.Key, kv => kv.Value.Session, StringComparer.OrdinalIgnoreCase);
            lock (_snapshotLock) _snapshot = new Snapshot(tools, config, sessions);
            return tools;
        }
        finally { _gate.Release(); }
    }

    private async Task<(McpServerConfig Server, Running? Running, string? Error)> StartAsync(McpServerConfig server, string fingerprint, CancellationToken ct)
    {
        IMcpSession? session = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(StartTimeout);
            session = await connect(server, timeout.Token);
            var list = await session.ListToolsAsync(timeout.Token);
            return (server, new Running(session, fingerprint, list), null);
        }
        catch (Exception ex) // never throws: results of servers that did start must not be lost (their processes would leak)
        {
            if (session is not null) await SafeDispose(session);
            if (ct.IsCancellationRequested) return (server, null, "cancelled");
            log.Error($"MCP server {server.Name} failed", ex);
            return (server, null, ex is OperationCanceledException ? $"didn't start within {StartTimeout.TotalSeconds:0} s" : ex.Message);
        }
    }

    public async Task<ToolResult> CallAsync(string apiName, JsonElement arguments, CancellationToken ct)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        var spec = snapshot.Tools.FirstOrDefault(t => t.ApiName == apiName);
        if (spec is null) return new ToolResult($"There is no tool named {apiName}.", true);
        var decision = ToolPolicy.Decide(Volatile.Read(ref _snapshot).Config.Approvals, spec.Server, spec.Tool, spec.ReadOnly);
        if (decision == ToolApproval.Deny) return new ToolResult($"The user doesn't allow {spec.Server}/{spec.Tool}.", true);
        if (decision == ToolApproval.Ask)
        {
            var answer = await approve(new ToolCallRequest(spec, arguments), ct);
            if (answer == ToolDecision.Deny) return new ToolResult($"The user declined running {spec.Server}/{spec.Tool}.", true);
            if (answer == ToolDecision.AllowAlways) Remember(spec);
        }
        if (!snapshot.Sessions.TryGetValue(spec.Server, out var session)) return new ToolResult($"The {spec.Server} tools aren't running.", true);
        try
        {
            var result = await session.CallAsync(spec.Tool, arguments, ct);
            log.Info($"tool {spec.Server}/{spec.Tool}: {(result.IsError ? "error" : "ok")} ({result.Text.Length} chars)");
            return result.Text.Length <= MaxResultChars ? result
                : result with { Text = result.Text[..MaxResultChars] + $"\n…(truncated, {result.Text.Length} chars total)" };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Includes a session that was restarted (its entry in mcp.json changed) while this call ran.
            log.Error($"tool {spec.Server}/{spec.Tool} failed", ex);
            return new ToolResult($"The tool failed: {ex.Message}", true);
        }
    }

    private void Remember(ToolSpec spec)
    {
        var key = $"{spec.Server}/{spec.Tool}";
        lock (_snapshotLock)
        {
            var config = _snapshot.Config;
            var approvals = new Dictionary<string, ToolApproval>(config.Approvals, StringComparer.OrdinalIgnoreCase) { [key] = ToolApproval.Allow };
            _snapshot = _snapshot with { Config = new McpConfig { Servers = config.Servers, Approvals = approvals, Error = config.Error } };
        }
        if (configPath is null) return;
        try { McpConfig.SaveApproval(configPath, key, ToolApproval.Allow); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { log.Error("saving the tool approval failed", ex); }
    }

    private bool RecentlyFailed(string name, string fingerprint, DateTimeOffset now)
    {
        lock (_failed)
            return _failed.TryGetValue(name, out var f) && f.Fingerprint == fingerprint && now - f.At < RetryFailedAfter;
    }

    private static string Fingerprint(McpServerConfig s) =>
        JsonSerializer.Serialize(new { s.Command, s.Args, s.Env, s.WorkingDirectory });

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void SetStatus(McpServerStatus status)
    {
        lock (_status) _status[status.Name] = status;
        StatusChanged?.Invoke();
    }

    private static async Task SafeDispose(IMcpSession session)
    {
        try { await session.DisposeAsync(); } catch (Exception) { /* already gone */ }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var running in _running.Values) await SafeDispose(running.Session);
            _running.Clear();
            lock (_snapshotLock) _snapshot = new Snapshot([], _snapshot.Config, new Dictionary<string, IMcpSession>());
            lock (_status) _status.Clear();
        }
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
