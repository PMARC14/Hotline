using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Tools;

/// <summary>
/// How an MCP server process is started. Like the SDK's StdioClientTransport (2.2.0) so configs behave the same, except
/// that on Windows only what isn't an .exe (<c>npx</c>/<c>.cmd</c> shims, a command not found) runs through
/// <c>cmd.exe /c</c>, and then only with arguments cmd passes as they are: cmd expands <c>%VAR%</c> even inside quotes
/// and doesn't treat <c>\"</c> as an escape, so a quote, <c>%</c>, <c>&amp;</c> and the like in mcp.json (or a
/// discovered server) could run extra commands. The environment is inherited with the server's entries on top.
/// We start the process ourselves so the app can put it in its kill-on-close job.
/// </summary>
public static partial class McpProcess
{
    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <param name="fileExists">Looks up the command the way cmd would (tests pass a fake file system).</param>
    /// <exception cref="IOException">The server would need cmd.exe and has a character it can't pass safely.</exception>
    public static ProcessStartInfo CreateStartInfo(McpServerConfig server, bool windows, Func<string, bool>? fileExists = null)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = server.WorkingDirectory ?? Environment.CurrentDirectory,
            StandardInputEncoding = NoBomUtf8,
            StandardOutputEncoding = NoBomUtf8,
            StandardErrorEncoding = NoBomUtf8,
        };
        foreach (var (k, v) in server.Env) psi.Environment[k] = v;
        var command = server.Command;
        IEnumerable<string> args = server.Args;
        // An explicit cmd.exe is the user's own command line, passed as written.
        if (windows && !string.Equals(Path.GetFileName(command), "cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = Platform.CommandResolver.Find(command, server.WorkingDirectory, psi.Environment.TryGetValue("PATH", out var path) ? path : null,
                psi.Environment.TryGetValue("PATHEXT", out var ext) ? ext : null, fileExists ?? File.Exists);
            if (resolved is not null && Path.GetExtension(resolved).ToLowerInvariant() is ".exe" or ".com")
                command = resolved; // started directly: arguments reach it exactly as written
            else
            {
                if (server.Args.Prepend(command).Any(CmdUnsafe().IsMatch))
                    throw new IOException(L.F(Strings.Mcp_UnsafeArgument, server.Name, command));
                args = ["/c", command, .. server.Args];
                command = "cmd.exe";
            }
        }
        psi.FileName = command;
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    [GeneratedRegex("[\"%^&|<>!\r\n]", RegexOptions.CultureInvariant)]
    private static partial Regex CmdUnsafe();
}

/// <summary>Forwards a server's stderr to the log, at most <c>perMinute</c> lines a minute, and keeps a short tail for errors.</summary>
public sealed class StderrThrottle(string server, Action<string> log, TimeProvider clock, int perMinute = 20, int tailLines = 5)
{
    private readonly Lock _gate = new();
    private readonly Queue<string> _tail = new();
    private DateTimeOffset _windowStart = DateTimeOffset.MinValue;
    private int _inWindow, _skipped;

    public string Tail { get { lock (_gate) return string.Join("\n", _tail); } }

    public void Line(string line)
    {
        string? skippedNote = null;
        bool pass;
        lock (_gate)
        {
            _tail.Enqueue(line);
            while (_tail.Count > tailLines) _tail.Dequeue();
            var now = clock.GetUtcNow();
            if (now - _windowStart >= TimeSpan.FromMinutes(1))
            {
                _windowStart = now;
                _inWindow = 0;
                if (_skipped > 0) skippedNote = $"{server}: ({_skipped} stderr lines skipped)";
                _skipped = 0;
            }
            pass = _inWindow < perMinute;
            if (pass) _inWindow++; else _skipped++;
        }
        if (skippedNote is not null) log(skippedNote);
        if (pass) log($"{server}: {line}");
    }
}

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
    public static Func<McpServerConfig, CancellationToken, Task<IMcpSession>> Connector(Action<Process>? onStarted, Action<string> log, TimeProvider? clock = null) =>
        (server, ct) => ConnectAsync(server, onStarted, log, clock ?? TimeProvider.System, ct);

    private static async Task<IMcpSession> ConnectAsync(McpServerConfig server, Action<Process>? onStarted, Action<string> log, TimeProvider clock, CancellationToken ct)
    {
        var stderr = new StderrThrottle(server.Name, log, clock);
        var startInfo = McpProcess.CreateStartInfo(server, OperatingSystem.IsWindows());
        // Not on this PC (no Node.js for npx, …): say what to install, rather than start cmd for "is not recognized".
        if (OperatingSystem.IsWindows() && !string.Equals(Path.GetFileName(server.Command), "cmd.exe", StringComparison.OrdinalIgnoreCase)
            && Platform.CommandResolver.Find(server.Command, server.WorkingDirectory, startInfo.Environment.TryGetValue("PATH", out var path) ? path : null,
                startInfo.Environment.TryGetValue("PATHEXT", out var ext) ? ext : null, File.Exists) is null)
            throw new IOException(Platform.CommandResolver.MissingMessage(server.Name, server.Command));
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.Line(e.Data); };
        bool started;
        Processes.ProcessLaunches.Noted();
        try { started = process.Start(); }
        catch (Exception ex)
        {
            process.Dispose();
            throw new IOException(L.F(Strings.Mcp_StartFailed, server.Command) + ": " + ex.Message, ex);
        }
        if (!started)
        {
            process.Dispose();
            throw new IOException(L.F(Strings.Mcp_StartFailed, server.Command));
        }
        McpClient? client = null;
        using var exitedOrCancelled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        EventHandler onExited = (_, _) => { try { exitedOrCancelled.Cancel(); } catch (ObjectDisposedException) { } };
        try
        {
            onStarted?.Invoke(process);
            process.BeginErrorReadLine();
            process.Exited += onExited;
            if (process.HasExited) exitedOrCancelled.Cancel();
            client = await McpClient.CreateAsync(new StreamClientTransport(process.StandardInput.BaseStream, process.StandardOutput.BaseStream),
                new McpClientOptions { ClientInfo = new Implementation { Name = "Hotline", Version = "1.0" } }, cancellationToken: exitedOrCancelled.Token);
            process.Exited -= onExited;
            return new StdioMcpSession(client, process);
        }
        catch (Exception ex)
        {
            process.Exited -= onExited;
            if (!ct.IsCancellationRequested)
            {
                // The SDK can see end-of-stream a moment before the exit; also lets the last stderr lines arrive.
                using var flush = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await process.WaitForExitAsync(flush.Token); } catch (OperationCanceledException) { }
            }
            var exited = HasExited(process);
            var message = exited ? ExitMessage(process, stderr) : null;
            await ShutdownAsync(process, client, TimeSpan.Zero);
            if (message is not null && !ct.IsCancellationRequested) throw new IOException(message, ex);
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
}
