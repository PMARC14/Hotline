using System.Text.Json;
using Hotline.Core.Diagnostics;

namespace Hotline.Core.Tools;

/// <summary>A tool offered to an API model. ApiName is what the model calls; Server/Tool identify the MCP tool.</summary>
public sealed record ToolSpec(string ApiName, string Server, string Tool, string Description, JsonElement InputSchema, bool ReadOnly);

public sealed record ToolResult(string Text, bool IsError);

public sealed record ToolCallRequest(ToolSpec Tool, JsonElement Arguments);

public enum ToolDecision { AllowOnce, AllowAlways, Deny }

/// <summary>
/// What the API backends see: the tools on offer and a way to call one (approval is handled inside). Calls pass the
/// exact <see cref="ToolSpec"/> the model was offered, so a refresh in between can't make a name mean another tool.
/// </summary>
public interface IToolHost
{
    Task<IReadOnlyList<ToolSpec>> GetToolsAsync(CancellationToken ct);
    Task<ToolResult> CallAsync(ToolSpec tool, JsonElement arguments, CancellationToken ct);
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
/// to mcp.json). Each server's tool list is cached while it runs; a server restarts only when its own entry changes or
/// it died, and one that failed to start is retried after <see cref="RetryFailedAfter"/> (or on
/// <see cref="RetryFailedServers"/>). If mcp.json can't be read, the last good config stays in force (deny rules
/// included); with no good config yet, no tools are offered.
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IMcpSession, byte> _dead = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _remembered = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _snapshotLock = new();
    private Snapshot _snapshot = new([], new Dictionary<string, IMcpSession>());
    private McpConfig? _lastGood;
    private string? _configError;
    private volatile bool _disposed;

    /// <summary>Connecting plus listing a server's tools must finish within this.</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryFailedAfter { get; init; } = TimeSpan.FromMinutes(1);

    private sealed record Running(IMcpSession Session, string Fingerprint, IReadOnlyList<McpToolInfo> Tools);

    /// <summary>What calls use: replaced as a whole, so a call never sees half of an update.</summary>
    private sealed record Snapshot(IReadOnlyList<ToolSpec> Tools, IReadOnlyDictionary<string, IMcpSession> Sessions);

    public IReadOnlyList<McpServerStatus> Status { get { lock (_status) return _status.Values.ToList(); } }
    public string? ConfigError => Volatile.Read(ref _configError);
    public event Action? StatusChanged;

    /// <summary>Forget start failures so the next <see cref="GetToolsAsync"/> tries those servers again ("Check servers").</summary>
    public void RetryFailedServers() { lock (_failed) _failed.Clear(); }

    public async Task<IReadOnlyList<ToolSpec>> GetToolsAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var config = EffectiveConfig();
            if (config is null)
            {
                // Never read successfully: fail closed rather than run tools without the user's rules.
                await StopAsync(_running.Keys.ToList());
                Publish([]);
                return [];
            }
            var servers = new List<McpServerConfig>();
            foreach (var server in config.Servers.Concat(extraServers()).DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (ToolNames.IsAmbiguous(server.Name))
                {
                    SetStatus(new(server.Name, server.DisplayName ?? server.Name, McpServerState.Failed, 0, "A server name can't contain '/' or '*'."));
                    continue;
                }
                servers.Add(server);
            }
            var fingerprints = servers.ToDictionary(s => s.Name, Fingerprint, StringComparer.OrdinalIgnoreCase);

            // Stop only servers that were removed, disabled, changed or died.
            await StopAsync(_running.Where(kv => !fingerprints.TryGetValue(kv.Key, out var fp) || fp != kv.Value.Fingerprint
                || servers.First(s => Same(s.Name, kv.Key)).Disabled || _dead.ContainsKey(kv.Value.Session)).Select(kv => kv.Key).ToList());
            foreach (var stale in _dead.Keys.Where(d => !_running.Values.Any(r => ReferenceEquals(r.Session, d))).ToList()) _dead.TryRemove(stale, out _);
            lock (_status)
                foreach (var name in _status.Keys.Where(k => !fingerprints.ContainsKey(k) && !ToolNames.IsAmbiguous(k)).ToList()) _status.Remove(name);
            Publish(Volatile.Read(ref _snapshot).Tools.Where(t => _running.ContainsKey(t.Server)).ToList()); // calls stop using stopped servers now

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

            var approvals = Approvals(config);
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
                    if (ToolNames.IsAmbiguous(t.Name)) { log.Error($"MCP server {server.Name}: skipped tool '{t.Name}' ('/' and '*' aren't allowed in names)"); continue; }
                    if (ToolPolicy.Decide(approvals, server.Name, t.Name, t.ReadOnly) == ToolApproval.Deny) continue; // never offered
                    tools.Add(new ToolSpec(ToolNames.ForApi(server.Name, t.Name, used), server.Name, t.Name, t.Description, t.InputSchema, t.ReadOnly));
                    offered++;
                }
                SetStatus(new(server.Name, display, McpServerState.Ready, offered, null));
            }
            Publish(tools);
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

    public async Task<ToolResult> CallAsync(ToolSpec tool, JsonElement arguments, CancellationToken ct)
    {
        if (_disposed) return new ToolResult("Hotline's tools are shutting down.", true);
        var snapshot = Volatile.Read(ref _snapshot);
        // The current entry, not the caller's copy: if the server now marks the tool as not read-only, that must ask.
        if (snapshot.Tools.FirstOrDefault(t => Same(t.Server, tool.Server) && t.Tool == tool.Tool) is not { } current
            || !snapshot.Sessions.TryGetValue(tool.Server, out var session))
            return new ToolResult($"{tool.Server}/{tool.Tool} is no longer available.", true);
        tool = current;
        // The policy is checked against the file as it is now (a deny added mid-answer applies to the next call).
        var config = EffectiveConfig();
        if (config is null) return new ToolResult("mcp.json can't be read, so no tools run until it's fixed.", true);
        var decision = ToolPolicy.Decide(Approvals(config), tool.Server, tool.Tool, tool.ReadOnly);
        if (decision == ToolApproval.Deny) return new ToolResult($"The user doesn't allow {tool.Server}/{tool.Tool}.", true);
        if (decision == ToolApproval.Ask)
        {
            var answer = await approve(new ToolCallRequest(tool, arguments), ct);
            if (answer == ToolDecision.Deny) return new ToolResult($"The user declined running {tool.Server}/{tool.Tool}.", true);
            if (answer == ToolDecision.AllowAlways) Remember(tool);
        }
        try
        {
            var result = await session.CallAsync(tool.Tool, arguments, ct);
            log.Info($"tool {tool.Server}/{tool.Tool}: {(result.IsError ? "error" : "ok")} ({result.Text.Length} chars)");
            return result.Text.Length <= MaxResultChars ? result
                : result with { Text = result.Text[..MaxResultChars] + $"\n…(truncated, {result.Text.Length} chars total)" };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The server process died or its connection closed: restart it on the next answer.
            if (ex is IOException or ObjectDisposedException or InvalidOperationException) _dead.TryAdd(session, 0);
            log.Error($"tool {tool.Server}/{tool.Tool} failed", ex);
            return new ToolResult($"The tool failed: {ex.Message}", true);
        }
    }

    /// <summary>The config in force: the file if it reads cleanly, else the last good one (null if there never was one).</summary>
    private McpConfig? EffectiveConfig()
    {
        var loaded = loadConfig();
        if (loaded.Error is null)
        {
            Volatile.Write(ref _lastGood, loaded);
            Volatile.Write(ref _configError, loaded.Warnings.Count > 0 ? string.Join(" ", loaded.Warnings) : null);
            return loaded;
        }
        var good = Volatile.Read(ref _lastGood);
        Volatile.Write(ref _configError, good is null ? $"{loaded.Error} No tools run until it's fixed." : $"{loaded.Error} The last good version stays in force.");
        return good;
    }

    /// <summary>The file's rules plus "Always" clicks whose save failed (an explicit rule in the file wins over those).</summary>
    private IReadOnlyDictionary<string, ToolApproval> Approvals(McpConfig config)
    {
        if (_remembered.IsEmpty) return config.Approvals;
        var merged = new Dictionary<string, ToolApproval>(config.Approvals, StringComparer.OrdinalIgnoreCase);
        foreach (var key in _remembered.Keys) merged.TryAdd(key, ToolApproval.Allow);
        return merged;
    }

    private void Remember(ToolSpec spec)
    {
        var key = $"{spec.Server}/{spec.Tool}";
        _remembered[key] = 0;
        if (configPath is null) return;
        try { McpConfig.SaveApproval(configPath, key, ToolApproval.Allow); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { log.Error("saving the tool approval failed", ex); }
    }

    private void Publish(IReadOnlyList<ToolSpec> tools)
    {
        var sessions = _running.ToDictionary(kv => kv.Key, kv => kv.Value.Session, StringComparer.OrdinalIgnoreCase);
        lock (_snapshotLock) _snapshot = new Snapshot(tools, sessions);
    }

    private async Task StopAsync(IReadOnlyList<string> names)
    {
        foreach (var name in names)
            if (_running.Remove(name, out var running))
            {
                _dead.TryRemove(running.Session, out _);
                await SafeDispose(running.Session);
            }
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

    /// <summary>Stops a session, but never waits more than a few seconds (a hung server must not freeze every tool).</summary>
    private static async Task SafeDispose(IMcpSession session)
    {
        try { await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { /* already gone, or hung: abandon it */ }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _disposed = true; // a GetToolsAsync waiting on the gate won't start servers again
            await StopAsync(_running.Keys.ToList());
            Publish([]);
            lock (_status) _status.Clear();
        }
        finally { _gate.Release(); }
    }
}
