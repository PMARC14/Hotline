using Hotline.Core.Text.Localization;
using System.Runtime.CompilerServices;
using System.Text;
using Hotline.Core.Chat;

namespace Hotline.Core.Backends.Api;

/// <summary>The server-sent-events reader of <see cref="ApiCommon"/>.</summary>
public static partial class ApiCommon
{
    /// <summary>
    /// Server-sent events: yields each event's data (multi-line data joined with \n). A line or event longer than
    /// <paramref name="maxChars"/> ends the stream with an error instead of growing without bound.
    /// </summary>
    public static async IAsyncEnumerable<string> SseData(Stream stream, [EnumeratorCancellation] CancellationToken ct,
        int maxChars = MaxSseChars, string name = "The server")
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        var line = new StringBuilder();
        var buffer = new char[4096];
        var skipLf = false; // the \n of a \r\n that straddled two reads
        int n;
        while ((n = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            var start = 0;
            while (start < n)
            {
                if (skipLf)
                {
                    skipLf = false;
                    if (buffer[start] == '\n') { start++; continue; }
                }
                var eol = buffer.AsSpan(start, n - start).IndexOfAny('\r', '\n');
                var count = eol < 0 ? n - start : eol;
                if (line.Length + count > maxChars) throw TooLarge(name);
                line.Append(buffer, start, count);
                if (eol < 0) break;
                skipLf = buffer[start + eol] == '\r';
                start += eol + 1;
                if (EndLine(line, data, maxChars, name) is { } ev) yield return ev;
            }
        }
        // The stream ended mid-line or right after one newline: that last line (a blank one completes the event) still counts.
        if (EndLine(line, data, maxChars, name) is { } last) yield return last;
        else if (data.Length > 0) yield return data.ToString();
    }

    /// <summary>One finished line: a blank line ends the event (returned), a data: line joins the event.</summary>
    private static string? EndLine(StringBuilder lineBuilder, StringBuilder data, int maxChars, string name)
    {
        if (lineBuilder.Length == 0)
        {
            if (data.Length == 0) return null;
            var ev = data.ToString();
            data.Clear();
            return ev;
        }
        var line = lineBuilder.ToString();
        lineBuilder.Clear();
        if (line.StartsWith("data:", StringComparison.Ordinal))
        {
            if (data.Length > 0) data.Append('\n');
            var value = line.AsSpan(5);
            data.Append(value.StartsWith(" ") ? value[1..] : value); // the spec strips one space: keep indentation
            if (data.Length > maxChars) throw TooLarge(name);
        }
        return null;
    }

    private static BackendException TooLarge(string name) => new(BackendErrorKind.Failed, L.F(Strings.Api_TooLarge, name));
}
