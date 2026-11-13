using System.Text;
using Hotline.Core.Chat;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Backends;

/// <summary>
/// Windows command-line length validation for CLI backends. Windows CreateProcess enforces a 32,767-character limit
/// on the entire escaped command line (executable, argument quoting/escaping, separators, and terminating null character).
/// </summary>
public static class CliCommandLine
{
    public const int MaxCommandLineLength = 32767;

    /// <summary>
    /// Formats an executable and its arguments according to standard Windows (CommandLineToArgvW) quoting and escaping rules.
    /// </summary>
    public static string FormatCommandLine(string exe, IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        AppendArgument(sb, exe);
        foreach (var arg in args)
        {
            AppendArgument(sb, arg);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Computes the serialized Windows command-line length, including executable, separators, argument escaping,
    /// and the terminating NUL character (+1).
    /// </summary>
    public static int ComputeCommandLineLength(string exe, IEnumerable<string> args) =>
        FormatCommandLine(exe, args).Length + 1;

    /// <summary>
    /// Rejects an oversized CLI launch command before starting the process, throwing BackendErrorKind.NotConfigured
    /// with Strings.Cli_CommandTooLong if the command exceeds 32,767 characters.
    /// </summary>
    public static void RequireFits(string exe, IEnumerable<string> args, string profileName)
    {
        if (ComputeCommandLineLength(exe, args) > MaxCommandLineLength)
            throw new BackendException(BackendErrorKind.NotConfigured, L.F(Strings.Cli_CommandTooLong, profileName));
    }

    private static void AppendArgument(StringBuilder sb, string argument)
    {
        if (sb.Length != 0) sb.Append(' ');
        if (argument.Length != 0 && !argument.Any(char.IsWhiteSpace) && !argument.Contains('"'))
        {
            sb.Append(argument);
            return;
        }

        sb.Append('"');
        int backslashes = 0;
        foreach (var c in argument)
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
