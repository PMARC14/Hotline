using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Tools;

/// <summary>A real MCP server over stdio: we own the process (see <see cref="McpProcess"/>), the SDK speaks the protocol.</summary>
public sealed class StdioMcpSession : IMcpSession
{
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(2);
    private readonly McpClient _client;
    private readonly Process _process;
    private int _disposed;

    private StdioMcpSession(McpClient client, Process process) => (_client, _process) = (client, process);

    /// <summary>The connect function for <see cref="McpToolHost"/>. <paramref name="onStarted"/> runs right after the
    /// process starts (the app adds it to its kill-on-close job); <paramref name="log"/> receives throttled stderr.</summary>
    public static Func<McpServerConfig, CancellationToken, Task<IMcpSession>> Connector(Action<Process>? onStarted, Action<string> log, TimeProvider? clock = null, Func<bool>? verbose = null) =>
        (server, ct) => ConnectAsync(server, onStarted, log, clock ?? TimeProvider.System, verbose, ct);

    private static async Task<IMcpSession> ConnectAsync(McpServerConfig server, Action<Process>? onStarted, Action<string> log, TimeProvider clock, Func<bool>? verbose, CancellationToken ct)
    {
        var scrubValues = McpSecrets.GetScrubValues(server);
        string Scrub(string s) => McpSecrets.Scrub(s, scrubValues);
        var stderr = new StderrThrottle(server.Name, log, clock, verbose: verbose, scrub: Scrub);
        var startInfo = McpProcess.CreateStartInfo(server, OperatingSystem.IsWindows());
        // Not on this PC (no Node.js for npx, …): say what to install, rather than start cmd for "is not recognized".
        if (OperatingSystem.IsWindows() && !string.Equals(Path.GetFileName(server.Command), "cmd.exe", StringComparison.OrdinalIgnoreCase)
            && Platform.CommandResolver.Find(server.Command, server.WorkingDirectory, startInfo.Environment.TryGetValue("PATH", out var path) ? path : null,
                startInfo.Environment.TryGetValue("PATHEXT", out var ext) ? ext : null, File.Exists) is null)
            throw new IOException(Scrub(Platform.CommandResolver.MissingMessage(server.Name, server.Command)));
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        bool started;
        Processes.ProcessLaunches.Noted();
        try { started = process.Start(); }
        catch (Exception ex)
        {
            process.Dispose();
            throw new IOException(Scrub(L.F(Strings.Mcp_StartFailed, server.Command) + ": " + ex.Message), ex);
        }
        if (!started)
        {
            process.Dispose();
            throw new IOException(Scrub(L.F(Strings.Mcp_StartFailed, server.Command)));
        }
        McpClient? client = null;
        using var exitedOrCancelled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        EventHandler onExited = (_, _) => { try { exitedOrCancelled.Cancel(); } catch (ObjectDisposedException) { } };
        try
        {
            onStarted?.Invoke(process);
            PumpStderr(process.StandardError, stderr.Line);
            process.Exited += onExited;
            if (process.HasExited) exitedOrCancelled.Cancel();
            client = await McpClient.CreateAsync(new StreamClientTransport(process.StandardInput.BaseStream, process.StandardOutput.BaseStream),
                new McpClientOptions { ClientInfo = new Implementation { Name = "Hotline", Version = "1.0" } }, cancellationToken: exitedOrCancelled.Token);
            process.Exited -= onExited;
            return new StdioMcpSession(client, process);
        }
        catch (Exception)
        {
            process.Exited -= onExited;
            if (!ct.IsCancellationRequested)
            {
                // The SDK can see end-of-stream a moment before the exit; also lets the last stderr lines arrive.
                using var flush = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await process.WaitForExitAsync(flush.Token); } catch (OperationCanceledException) { }
            }
            var exited = HasExited(process);
            var message = exited ? Scrub(ExitMessage(process, stderr)) : null;
            await ShutdownAsync(process, client, TimeSpan.Zero);
            if (message is not null && !ct.IsCancellationRequested) throw new IOException(message);
            throw;
        }
    }

    private static string ExitMessage(Process process, StderrThrottle stderr)
    {
        string code;
        try { code = process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture); } catch (InvalidOperationException) { code = "?"; }
        var tail = stderr.Tail;
        return L.F(Strings.Mcp_Exited, code) + (tail.Length > 0 ? ": " + tail.Replace("\n", " / ") : "");
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; } catch (InvalidOperationException) { return true; }
    }

    /// <summary>Close stdin (servers exit on EOF), give it a moment, then kill the whole tree.</summary>
    private static async Task ShutdownAsync(Process process, McpClient? client, TimeSpan grace)
    {
        try { process.StandardInput.Close(); } catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException) { }
        if (grace > TimeSpan.Zero && !HasExited(process))
        {
            using var cts = new CancellationTokenSource(grace);
            try { await process.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { }
        }
        try
        {
            // AggregateException: part of the tree couldn't be killed (e.g. an elevated helper); the job still has it.
            try { if (!HasExited(process)) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or AggregateException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            if (client is not null)
            {
                try { await client.DisposeAsync(); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
        }
        finally { process.Dispose(); }
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await ShutdownAsync(_process, _client, ShutdownGrace);
    }

    /// <summary>Reads stderr in chunks and hands on lines of at most 4000 chars: a server that never writes a newline can't grow memory.</summary>
    internal static void PumpStderr(TextReader reader, Action<string> line) => _ = Task.Run(async () =>
    {
        const int MaxLine = 4000;
        var buffer = new char[4096];
        var current = new System.Text.StringBuilder();
        try
        {
            int n;
            while ((n = await reader.ReadAsync(buffer)) > 0)
                for (var i = 0; i < n; i++)
                {
                    var c = buffer[i];
                    if (c == '\n') { line(current.ToString().TrimEnd('\r')); current.Clear(); }
                    else if (current.Length < MaxLine) current.Append(c);
                }
            if (current.Length > 0) line(current.ToString().TrimEnd('\r'));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    });
}
