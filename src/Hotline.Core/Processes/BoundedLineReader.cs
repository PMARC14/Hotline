using System.Text;

namespace Hotline.Core.Processes;

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
