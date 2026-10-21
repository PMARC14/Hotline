using System.Text;
using System.Text.RegularExpressions;

namespace Hotline.Core.Diagnostics;

/// <summary>
/// What may be written to the log. Every line goes through <see cref="Clean"/>: line breaks are indented (a message can't forge
/// a log line), control characters are replaced, a huge message is cut, and anything shaped like a secret (bearer tokens, API
/// keys, key= parameters, passwords in URLs) is replaced. Hotline already logs lengths and ids rather than your text and sends
/// keys in headers; this is the safety net for what a library, a CLI or a server puts in an error message.
/// </summary>
public static class LogText
{
    private const int MaxLength = 16_000;

    // NonBacktracking: linear time whatever the input, so a hostile message can't stall the logger.
    private static Regex Rule(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly (Regex Pattern, string Replacement)[] Secrets =
    [
        (Rule(@"\bbearer\s+[A-Za-z0-9._~+/=\-]{12,}"), "Bearer [redacted]"),
        (Rule(@"\b(authorization|x-api-key|x-goog-api-key|api[_-]?key|access[_-]?token|auth[_-]?token|token|secret|password|passwd)([""']?\s*[:=]\s*[""']?)[^\s""',;&]{6,}"), "$1$2[redacted]"),
        (Rule(@"\b(?:sk-[A-Za-z0-9_\-]{16,}|AIza[0-9A-Za-z_\-]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|xox[abprs]-[A-Za-z0-9\-]{10,})"), "[redacted]"),
        (Rule(@"(://)[^/\s:@]+:[^/\s@]+@"), "$1[redacted]@"),
        (Rule(@"([?&](?:key|api_key|apikey|token|access_token)=)[^&\s]+"), "$1[redacted]"),
    ];

    public static string Clean(string? message)
    {
        if (string.IsNullOrEmpty(message)) return "";
        var cut = message.Length > MaxLength;
        var text = cut ? message[..MaxLength] : message;

        var sb = new StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') continue; // CRLF is one break
            if (IsBreak(c)) sb.Append("\n    "); // continuation lines never start a log line
            else if (c == '\t' || !char.IsControl(c)) sb.Append(c);
            else sb.Append('?');
        }
        var clean = sb.ToString();
        foreach (var (pattern, replacement) in Secrets) clean = pattern.Replace(clean, replacement);
        return cut ? $"{clean} … (truncated {message.Length - MaxLength} chars)" : clean;
    }

    // Written as numbers: NEL and the Unicode line/paragraph separators end a line for the C# compiler even inside a character literal.
    private static bool IsBreak(char c) => c is '\r' or '\n' || c == (char)0x85 || c == (char)0x2028 || c == (char)0x2029;

    /// <summary>A Hotline link for the log: scheme, host and path, never the query (App Actions links carry your text or images).</summary>
    public static string SafeUri(Uri? uri)
    {
        if (uri is null) return "(none)";
        if (!uri.IsAbsoluteUri) return "(relative link)";
        var query = uri.Query.Length > 1 ? $" (+{uri.Query.Length - 1} chars of data not logged)" : "";
        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}{query}";
    }
}
