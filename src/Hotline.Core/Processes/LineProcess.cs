using System.Diagnostics;
using System.Text;
using Hotline.Core.Chat;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Processes;

/// <summary>A child process driven by newline-delimited stdin/stdout (agy, later claude).</summary>
public interface ILineProcess : IAsyncDisposable
{
    bool HasExited { get; }
    string StandardErrorTail { get; }
    Task WriteLineAsync(string line, CancellationToken ct);
    /// <summary>Closes stdin, for one-shot programs that read their input to the end (codex exec).</summary>
    void CompleteInput();
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
    /// <summary>Longest stdout line read (16M chars): a stream-json event is far smaller, even with images in it.</summary>
    public const int MaxLineChars = 16 * 1024 * 1024;
    private readonly Process _process;
    private readonly StringBuilder _stderr = new();
    private readonly BoundedLineReader _stdout;
    private readonly string _name;

    public SystemLineProcess(string exe, IReadOnlyList<string> args, string workingDirectory, Action<Process>? onStarted, int maxLineChars = MaxLineChars)
    {
        _name = Path.GetFileNameWithoutExtension(exe);
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
        ProcessLaunches.Noted();
        _process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
        try
        {
            onStarted?.Invoke(_process);
            _stdout = new BoundedLineReader(_process.StandardOutput, maxLineChars);
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
        catch
        {
            // Nothing owns the child yet (the caller never gets this object): don't leave it running.
            try { _process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException) { }
            _process.Dispose();
            throw;
        }
    }

    public bool HasExited => _process.HasExited;

    public string StandardErrorTail { get { lock (_stderr) return _stderr.ToString().Trim(); } }

    public async Task WriteLineAsync(string line, CancellationToken ct)
    {
        await _process.StandardInput.WriteAsync((line + "\n").AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
    }

    public void CompleteInput()
    {
        try { _process.StandardInput.Close(); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { /* already exited or closed */ }
    }

    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        try { return await _stdout.ReadLineAsync(ct); }
        catch (OutputTooLongException)
        {
            // The turn can't be parsed any more: stop the CLI (the backends start a new one for the next turn).
            await KillAsync();
            throw new BackendException(BackendErrorKind.Failed, L.F(Strings.Cli_OutputTooLong, _name));
        }
    }

    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try { await _process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { /* still running; take what we have */ }
    }

    private async Task KillAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await WaitForExitAsync(TimeSpan.FromSeconds(3)); // bounded: a stray grandchild holding the pipes must not hang cancel/quit
            }
        }
        // AggregateException: a child of the CLI exited (or couldn't be opened) while the tree was being killed.
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException) { /* already gone / exiting */ }
    }

    public async ValueTask DisposeAsync()
    {
        try { await KillAsync(); }
        finally { _process.Dispose(); }
    }
}

/// <summary>A program printed more than Hotline reads at once (one line, or a whole output).</summary>
public sealed class OutputTooLongException(int maxChars) : IOException($"output longer than {maxChars} characters");

/// <summary>
/// Reads lines like <see cref="StreamReader.ReadLineAsync(CancellationToken)"/> (\n, \r\n or \r ends one), but a line
/// longer than <paramref name="maxChars"/> throws instead of growing: a CLI printing without a newline must not take
/// all memory.
/// </summary>
public sealed class BoundedLineReader(TextReader reader, int maxChars)
{
    private readonly char[] _buffer = new char[4096];
    private int _pos, _length;
    private bool _skipNewline; // the last line ended in \r: a \n right after it belongs to that line
    private StringBuilder? _partial; // the start of a line that spans reads: kept here so a cancelled read doesn't lose it

    /// <summary>Next line, or null at the end.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            if (_pos == _length)
            {
                _length = await reader.ReadAsync(_buffer.AsMemory(), ct);
                _pos = 0;
                if (_length == 0) return TakePartial(); // an unterminated last line, or the end
            }
            if (_skipNewline)
            {
                _skipNewline = false;
                if (_buffer[_pos] == '\n') { _pos++; continue; }
            }
            var start = _pos;
            while (_pos < _length && _buffer[_pos] is not ('\n' or '\r')) _pos++;
            var count = _pos - start;
            if ((_partial?.Length ?? 0) + count > maxChars) { _partial = null; throw new OutputTooLongException(maxChars); }
            if (_pos == _length)
            {
                (_partial ??= new StringBuilder()).Append(_buffer, start, count);
                continue;
            }
            _skipNewline = _buffer[_pos++] == '\r';
            if (_partial is null) return new string(_buffer, start, count);
            _partial.Append(_buffer, start, count);
            return TakePartial();
        }
    }

    private string? TakePartial()
    {
        var text = _partial?.ToString();
        _partial = null;
        return text;
    }

    /// <summary>The whole output, or <see cref="OutputTooLongException"/> past <paramref name="maxChars"/>.</summary>
    public static async Task<string> ReadToEndAsync(TextReader reader, int maxChars, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (sb.Length + read > maxChars) throw new OutputTooLongException(maxChars);
            sb.Append(buffer, 0, read);
        }
        return sb.ToString();
    }
}
