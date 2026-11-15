using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Diagnostics;
using Hotline.Core.State;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Tools;

/// <summary>A tool offered to an API model. ApiName is what the model calls; Server/Tool identify the MCP tool.</summary>
public sealed record ToolSpec(string ApiName, string Server, string Tool, string Description, JsonElement InputSchema, bool ReadOnly);

public sealed record ToolResult(string Text, bool IsError);

public sealed record ToolCallRequest(ToolSpec Tool, JsonElement Arguments, bool AllowAlways = true);

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
/// resolves ${hotline:secret:&lt;name&gt;} env tokens at launch, keeps going if one fails, applies the approval policy
/// before every call (rules > grants > default; "Always allow" stores a launch-hashed grant via IGrantStore).
/// </summary>
public sealed class McpToolHost(
    Func<McpConfig> loadConfig,
    Func<IReadOnlyList<McpServerConfig>> extraServers,
    Func<IReadOnlyDictionary<string, ToolApproval>> getRules,
    IGrantStore grants,
    ISecretStore secrets,
    Func<McpServerConfig, CancellationToken, Task<IMcpSession>> connect,
    Func<ToolCallRequest, CancellationToken, Task<ToolDecision>> approve,
    FileLog log,
    TimeProvider? time = null) : IToolHost, IAsyncDisposable
{
    private const int MaxResultChars = 32_000;
    private static readonly IReadOnlyDictionary<string, ToolApproval> EmptyRules =
        new Dictionary<string, ToolApproval>(StringComparer.Ordinal);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Running> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Fingerprint, DateTimeOffset At)> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, McpServerStatus> _status = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IMcpSession, byte> _dead = new();
    private readonly HashSet<string> _warnedCaseRules = new(StringComparer.Ordinal);
    private readonly Lock _snapshotLock = new();
    private Snapshot _snapshot = new([], new Dictionary<string, Running>());
    private McpConfig? _lastGood;
    private string? _configError;
    private volatile bool _disposed;

    /// <summary>Connecting plus listing a server's tools must finish within this.</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryFailedAfter { get; init; } = TimeSpan.FromMinutes(1);

    private sealed record Running(IMcpSession Session, string Fingerprint, string LaunchHash, IReadOnlyList<string> SensitiveValues, IReadOnlyList<McpToolInfo> Tools);

    /// <summary>What calls use: replaced as a whole, so a call never sees half of an update.</summary>
    private sealed record Snapshot(IReadOnlyList<ToolSpec> Tools, IReadOnlyDictionary<string, Running> Sessions);

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
                // Never read successfully: fail closed rather than run tools without server configuration.
                await StopAsync(_running.Keys.ToList());
                Publish([]);
                return [];
            }
            var servers = new List<McpServerConfig>();
            foreach (var server in config.Servers.Concat(extraServers()).DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (ToolNames.IsAmbiguous(server.Name))
                {
                    SetStatus(new(server.Name, server.DisplayName ?? server.Name, McpServerState.Failed, 0, Strings.Mcp_BadName));
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

            var rules = getRules() ?? EmptyRules;
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
                    var decision = ToolPolicy.Evaluate(rules, grants, server.Name, t.Name, t.ReadOnly, running.LaunchHash);
                    WarnIfCaseMismatch(server.Name, t.Name, decision.CaseMismatchAllowRule);
                    if (decision.Approval == ToolApproval.Deny) continue; // never offered
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
        if (!McpSecrets.TryResolveEnv(server, secrets, out var resolvedEnv, out var secretError))
        {
            log.Error($"MCP server {server.Name}: {secretError}");
            return (server, null, secretError);
        }

        var launchHash = StateStore.ComputeLaunchHash(server.Command, server.Args, server.WorkingDirectory);
        var sensitive = McpSecrets.CollectScrubValues(server, resolvedEnv);
        McpSecrets.AttachScrubValues(resolvedEnv, sensitive);
        IMcpSession? session = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(StartTimeout);
            var launchServer = ReferenceEquals(resolvedEnv, server.Env) ? server : server with { Env = resolvedEnv };
            session = await connect(launchServer, timeout.Token);
            var list = await session.ListToolsAsync(timeout.Token);
            return (server, new Running(session, fingerprint, launchHash, sensitive, list), null);
        }
        catch (Exception ex) // never throws: results of servers that did start must not be lost (their processes would leak)
        {
            if (session is not null) await SafeDispose(session);
            if (ct.IsCancellationRequested) return (server, null, "cancelled");
            var safeDetail = McpSecrets.Scrub(ex.ToString(), sensitive);
            log.Error($"MCP server {server.Name} failed: {safeDetail}");
            var statusMessage = ex is OperationCanceledException
                ? L.F(Strings.Mcp_StartTimeout, StartTimeout.TotalSeconds)
                : McpSecrets.Scrub(ex.Message, sensitive);
            return (server, null, statusMessage);
        }
    }

    public async Task<ToolResult> CallAsync(ToolSpec tool, JsonElement arguments, CancellationToken ct)
    {
        if (_disposed) return new ToolResult("Hotline's tools are shutting down.", true);
        var snapshot = Volatile.Read(ref _snapshot);
        // The current entry, not the caller's copy: if the server now marks the tool as not read-only, that must ask.
        if (snapshot.Tools.FirstOrDefault(t => Same(t.Server, tool.Server) && t.Tool == tool.Tool) is not { } current
            || !snapshot.Sessions.TryGetValue(tool.Server, out var running))
            return new ToolResult($"{tool.Server}/{tool.Tool} is no longer available.", true);
        tool = current;
        // Policy is checked against live rules, grants, and the launch hash of the running process.
        if (CheckPolicy(tool, running, out var decision) is { } blocked) return blocked;
        if (decision.Approval == ToolApproval.Ask)
        {
            var answer = await approve(new ToolCallRequest(tool, arguments, decision.CanAlwaysAllow), ct);
            if (answer == ToolDecision.Deny) return new ToolResult($"The user declined running {tool.Server}/{tool.Tool}.", true);
            // Don't persist "Always" or run if the rule or server launch config changed while the dialog was open.
            if (CheckPolicy(tool, running, out var afterDecision) is { } changed) return changed;
            if (answer == ToolDecision.AllowAlways && decision.CanAlwaysAllow && afterDecision.CanAlwaysAllow)
                grants.AddGrant(tool.Server, tool.Tool, running.LaunchHash);
        }
        try
        {
            if (CheckPolicy(tool, running, out _) is { } changed) return changed;
            var result = await running.Session.CallAsync(tool.Tool, arguments, ct);
            log.Info($"tool {tool.Server}/{tool.Tool}: {(result.IsError ? "error" : "ok")} ({result.Text.Length} chars)");
            return result.Text.Length <= MaxResultChars ? result
                : result with { Text = result.Text[..MaxResultChars] + $"\n…(truncated, {result.Text.Length} chars total)" };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The server process died or its connection closed: restart it on the next answer.
            if (ex is IOException or ObjectDisposedException or InvalidOperationException) _dead.TryAdd(running.Session, 0);
            var safeDetail = McpSecrets.Scrub(ex.ToString(), running.SensitiveValues);
            log.Error($"tool {tool.Server}/{tool.Tool} failed: {safeDetail}");
            return new ToolResult($"The tool failed: {McpSecrets.Scrub(ex.Message, running.SensitiveValues)}", true);
        }
    }

    private ToolResult? CheckPolicy(ToolSpec tool, Running running, out ToolPolicyDecision decision)
    {
        decision = new ToolPolicyDecision(ToolApproval.Deny, CanAlwaysAllow: false);
        var config = EffectiveConfig();
        if (config is null) return new ToolResult("mcp.json can't be read, so no tools run until it's fixed.", true);
        var server = config.Servers.Concat(extraServers()).FirstOrDefault(s => Same(s.Name, tool.Server));
        if (server is null) return new ToolResult($"{tool.Server} is no longer configured.", true);
        if (server.Disabled) return new ToolResult($"{tool.Server} is switched off in mcp.json.", true);
        var currentHash = StateStore.ComputeLaunchHash(server.Command, server.Args, server.WorkingDirectory);
        if (!string.Equals(currentHash, running.LaunchHash, StringComparison.OrdinalIgnoreCase))
            return new ToolResult($"{tool.Server} configuration changed; refresh tools before calling.", true);
        var rules = getRules() ?? EmptyRules;
        decision = ToolPolicy.Evaluate(rules, grants, tool.Server, tool.Tool, tool.ReadOnly, running.LaunchHash);
        WarnIfCaseMismatch(tool.Server, tool.Tool, decision.CaseMismatchAllowRule);
        return decision.Approval == ToolApproval.Deny ? new ToolResult($"The user doesn't allow {tool.Server}/{tool.Tool}.", true) : null;
    }

    private void WarnIfCaseMismatch(string server, string tool, string? ruleKey)
    {
        if (ruleKey is null) return;
        lock (_warnedCaseRules)
        {
            if (!_warnedCaseRules.Add($"{ruleKey}\0{server}/{tool}")) return;
        }
        log.Info(L.F(Strings.Mcp_CaseMismatchAllow, ruleKey, $"{server}/{tool}"));
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
        Volatile.Write(ref _configError, L.F(good is null ? Strings.Mcp_ConfigBroken : Strings.Mcp_ConfigBrokenKept, loaded.Error));
        return good;
    }

    private void Publish(IReadOnlyList<ToolSpec> tools)
    {
        var sessions = _running.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
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
