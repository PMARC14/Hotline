using Hotline.Core.Diagnostics;

namespace Hotline.Core.Tools;

/// <summary>Logs stderr counts (cleaned excerpts only with verbose logging), throttles bursts, and keeps a short tail for errors.</summary>
public sealed class StderrThrottle(string server, Action<string> log, TimeProvider clock, int perMinute = 20, int tailLines = 5,
    Func<bool>? verbose = null)
{
    private readonly Lock _gate = new();
    private readonly Queue<string> _tail = new();
    private DateTimeOffset _windowStart = DateTimeOffset.MinValue;
    private DateTimeOffset _hiddenAt = DateTimeOffset.MinValue;
    private int _inWindow, _skipped, _lines;

    public string Tail { get { lock (_gate) return string.Join("\n", _tail); } }

    public void Line(string line)
    {
        string? skippedNote = null;
        string? note = null;
        var showText = verbose?.Invoke() ?? false;
        lock (_gate)
        {
            _tail.Enqueue(line);
            while (_tail.Count > tailLines) _tail.Dequeue();
            var now = clock.GetUtcNow();
            var count = _lines + 1;
            if (now - _windowStart >= TimeSpan.FromMinutes(1))
            {
                count = Math.Max(1, _lines);
                _windowStart = now;
                _inWindow = 0;
                _lines = 0;
                if (_skipped > 0) skippedNote = $"{server}: ({_skipped} stderr lines skipped)";
                _skipped = 0;
            }
            _lines++;
            var pass = _inWindow < perMinute;
            if (pass) _inWindow++; else _skipped++;
            if (pass && showText)
            {
                var clean = LogText.Clean(line);
                note = $"{server}: stderr line {_lines}: {clean[..Math.Min(120, clean.Length)]}";
            }
            else if (pass && now - _hiddenAt >= TimeSpan.FromMinutes(1))
            {
                _hiddenAt = now;
                note = $"{server}: {count} stderr lines (hidden: turn on verbose logging to see them)";
            }
        }
        if (skippedNote is not null) log(skippedNote);
        if (note is not null) log(note);
    }
}
