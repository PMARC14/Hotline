using System.Diagnostics;
using System.Text;

namespace Hotline.Core.Processes;

/// <summary>A child process driven by newline-delimited stdin/stdout (agy, later claude).</summary>
public interface ILineProcess : IAsyncDisposable
{
    bool HasExited { get; }
    string StandardErrorTail { get; }
    Task WriteLineAsync(string line, CancellationToken ct);
    /// <summary>Next stdout line, or null when the process has ended.</summary>
    Task<string?> ReadLineAsync(CancellationToken ct);
    /// <summary>Waits (bounded) for the process to exit, e.g. so its final stderr has been collected.</summary>
    Task WaitForExitAsync(TimeSpan timeout);
}

public interface ILineProcessFactory
{
    ILineProcess Start(string exe, IReadOnlyList<string> args, string workingDirectory);
}

public sealed class SystemLineProcessFactory(Action<Process>? onStarted = null) : ILineProcessFactory
{
    public ILineProcess Start(string exe, IReadOnlyList<string> args, string workingDirectory) =>
        new SystemLineProcess(exe, args, workingDirectory, onStarted);
}

public sealed class SystemLineProcess : ILineProcess
{
    private const int StderrKeep = 4000;
    private readonly Process _process;
    private readonly StringBuilder _stderr = new();

    public SystemLineProcess(string exe, IReadOnlyList<string> args, string workingDirectory, Action<Process>? onStarted)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), // agy rejects a BOM
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        _process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
        onStarted?.Invoke(_process);
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_stderr)
            {
                _stderr.AppendLine(e.Data);
                if (_stderr.Length > StderrKeep) _stderr.Remove(0, _stderr.Length - StderrKeep);
            }
        };
        _process.BeginErrorReadLine();
    }

    public bool HasExited => _process.HasExited;

    public string StandardErrorTail { get { lock (_stderr) return _stderr.ToString().Trim(); } }

    public async Task WriteLineAsync(string line, CancellationToken ct)
    {
        await _process.StandardInput.WriteAsync((line + "\n").AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
    }

    public Task<string?> ReadLineAsync(CancellationToken ct) => _process.StandardOutput.ReadLineAsync(ct).AsTask();

    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try { await _process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { /* still running; take what we have */ }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await WaitForExitAsync(TimeSpan.FromSeconds(3)); // bounded: a stray grandchild holding the pipes must not hang cancel/quit
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { /* already gone / exiting */ }
        _process.Dispose();
    }
}
