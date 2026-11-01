using System.Text.RegularExpressions;

namespace Hotline.Core.Tools;

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
