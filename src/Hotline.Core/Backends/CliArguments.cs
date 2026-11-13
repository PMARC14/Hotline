using System.Text;

namespace Hotline.Core.Backends;

/// <summary>
/// Quote-aware command-line argument splitter and reversible joiner for CLI launch arguments and extra arguments.
/// Preserves embedded quotes (such as raw Codex -c TOML values), respects quoted paths with spaces,
/// and round-trips saved argument arrays.
/// </summary>
public static class CliArguments
{
    /// <summary>
    /// Parses a command-line string into distinct argument tokens, respecting double-quoted substrings
    /// and Windows-style backslash escaping for quotes.
    /// </summary>
    public static IReadOnlyList<string> Split(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;
        bool startsWithQuote = false;

        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                    hasToken = false;
                    startsWithQuote = false;
                }
                continue;
            }

            if (!hasToken)
            {
                hasToken = true;
                if (c == '"')
                {
                    startsWithQuote = true;
                    inQuotes = true;
                    continue;
                }
            }

            if (c == '\\')
            {
                int backslashes = 0;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    backslashes++;
                    i++;
                }
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    sb.Append('\\', backslashes / 2);
                    if (backslashes % 2 != 0)
                    {
                        // Escaped quote: literal quote inside argument
                        sb.Append('"');
                    }
                    else
                    {
                        // Quote is a delimiter
                        if (inQuotes)
                        {
                            inQuotes = false;
                            if (!startsWithQuote) sb.Append('"');
                        }
                        else
                        {
                            inQuotes = true;
                            if (!startsWithQuote) sb.Append('"');
                        }
                    }
                }
                else
                {
                    sb.Append('\\', backslashes);
                    i--; // step back since loop increments i
                }
            }
            else if (c == '"')
            {
                if (inQuotes)
                {
                    inQuotes = false;
                    if (!startsWithQuote) sb.Append('"');
                }
                else
                {
                    inQuotes = true;
                    if (!startsWithQuote) sb.Append('"');
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (hasToken)
        {
            tokens.Add(sb.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// Parses an ExtraArgs string into tokens, preserving embedded quotes (e.g. -c key="value").
    /// </summary>
    public static IReadOnlyList<string> SplitExtra(string? commandLine) => Split(commandLine);

    /// <summary>
    /// Reversibly joins argument tokens into a command-line string for display and editing in Settings.
    /// Arguments containing whitespace or starting with a quote are quoted; internal quotes and trailing
    /// backslashes are escaped.
    /// </summary>
    public static string Join(IEnumerable<string>? args)
    {
        if (args is null) return "";
        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            AppendJoinedArgument(sb, arg);
        }
        return sb.ToString();
    }

    private static void AppendJoinedArgument(StringBuilder sb, string arg)
    {
        bool needsQuotes = arg.Length == 0 || arg.Any(char.IsWhiteSpace) || arg.StartsWith('"');
        if (!needsQuotes)
        {
            sb.Append(arg);
            return;
        }

        sb.Append('"');
        int backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
            }
            else if (c == '"')
            {
                sb.Append('\\', 2 * backslashes + 1);
                sb.Append('"');
                backslashes = 0;
            }
            else
            {
                sb.Append('\\', backslashes);
                sb.Append(c);
                backslashes = 0;
            }
        }
        sb.Append('\\', 2 * backslashes);
        sb.Append('"');
    }
}
