using System.Threading.Channels;
using Hotline.Core.Processes;

namespace Hotline.Core.Tests;

/// <summary>In-memory agy stand-in: records written lines; the test scripts output lines per turn.</summary>
public sealed class FakeLineProcess : ILineProcess
{
    private readonly Channel<string?> _out = Channel.CreateUnbounded<string?>();
    public List<string> Written { get; } = [];
    public Func<string, IEnumerable<string>>? Respond { get; set; }
    public bool HasExited { get; private set; }
    public bool Disposed { get; private set; }
    public string StandardErrorTail { get; set; } = "";

    public Task WriteLineAsync(string line, CancellationToken ct)
    {
        Written.Add(line);
        foreach (var o in Respond?.Invoke(line) ?? []) _out.Writer.TryWrite(o);
        return Task.CompletedTask;
    }

    public async Task<string?> ReadLineAsync(CancellationToken ct) => await _out.Reader.ReadAsync(ct);

    /// <summary>Simulates the process dying: pending/next reads return null.</summary>
    public void Exit() { HasExited = true; _out.Writer.TryWrite(null); }

    public ValueTask DisposeAsync() { Disposed = true; HasExited = true; _out.Writer.TryWrite(null); return ValueTask.CompletedTask; }
}

public sealed class FakeLineProcessFactory : ILineProcessFactory
{
    public List<(string Exe, IReadOnlyList<string> Args, string Cwd, FakeLineProcess Process)> Started { get; } = [];
    public Func<FakeLineProcess>? Create { get; set; }

    public ILineProcess Start(string exe, IReadOnlyList<string> args, string workingDirectory)
    {
        var p = Create?.Invoke() ?? new FakeLineProcess();
        Started.Add((exe, args, workingDirectory, p));
        return p;
    }
}
