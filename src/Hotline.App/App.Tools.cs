using Hotline.App.Chat;
using Hotline.App.Interop;
using Hotline.Core.Processes;

namespace Hotline.App;

public partial class App
{
    /// <summary>
    /// MCP tools for API connections: servers from ~/.hotline/mcp.json plus the Windows on-device agent registry's
    /// connectors when odr.exe exists (discovered once in the background). Approvals go to the panel.
    /// </summary>
    private Hotline.Core.Tools.McpToolHost ToolHost(string dataDir, ChildProcessJob job)
    {
        var mcpPath = Path.Combine(dataDir, "mcp.json");
        var grants = new Hotline.Core.State.StateStore(Path.Combine(dataDir, Hotline.Core.State.StateStore.FileName));
        _odrPath = Hotline.Core.Tools.OdrDiscovery.FindOdr(File.Exists, Environment.GetEnvironmentVariable("WINDIR"), Environment.GetEnvironmentVariable("LOCALAPPDATA"));
        _log?.Info(_odrPath is null ? "Windows agent registry: not available on this Windows" : $"Windows agent registry: {_odrPath}");
        if (_odrPath is { } odr)
            _ = Task.Run(async () =>
            {
                foreach (var args in new[] { new[] { "mcp", "list" }, new[] { "list" } })
                {
                    try
                    {
                        var json = await CliRunner.RunAsync(odr, args, TimeSpan.FromSeconds(20), CancellationToken.None);
                        var servers = Hotline.Core.Tools.OdrDiscovery.Parse(json);
                        if (servers.Count == 0) continue;
                        _odrServers = servers;
                        _log?.Info($"Windows agent registry: {servers.Count} connector(s)");
                        return;
                    }
                    catch (Exception ex) { _log?.Error($"odr {string.Join(' ', args)} failed", ex); }
                }
            });
        _toolHost = new Hotline.Core.Tools.McpToolHost(
            () => Hotline.Core.Tools.McpConfig.Load(mcpPath), () => _odrServers,
            ToolRules, // settings.json tools.approvals, read anew for every check
            grants, _secrets!,
            Hotline.Core.Tools.StdioMcpSession.Connector(job.Add, line => _log?.Info($"mcp {line}"), verbose: () => _log?.Verbose == true),
            (request, ct) => _presenter?.AskToolApprovalAsync(request, ct) ?? Task.FromResult(Hotline.Core.Tools.ToolDecision.Deny),
            _log!);
        return _toolHost;
    }

    /// <summary>MCP servers are started by the MCP SDK (not in the kill-on-close job): stop them before a restart.</summary>
    private void StopToolServers()
    {
        try { if (_toolHost is { } host) Task.Run(async () => await host.DisposeAsync()).Wait(TimeSpan.FromSeconds(3)); }
        catch (Exception ex) { _log?.Error("stopping MCP servers failed", ex); }
    }

    private string? _toolRuleWarnings;

    /// <summary>The hand-written rules of settings.json (tools.approvals). An unknown word counts as deny; each new set of
    /// warnings is logged once.</summary>
    private IReadOnlyDictionary<string, Hotline.Core.Tools.ToolApproval> ToolRules()
    {
        var rules = Hotline.Core.Tools.ToolRules.Parse(_settingsService?.Current.Tools.Approvals, out var warnings);
        var text = string.Join("; ", warnings);
        if (text != _toolRuleWarnings)
        {
            _toolRuleWarnings = text;
            if (text.Length > 0) _log?.Error($"tools.approvals: {text}");
        }
        return rules;
    }
}
