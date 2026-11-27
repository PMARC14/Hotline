using System.Text.RegularExpressions;

namespace Hotline.Core.Backends;

/// <summary>
/// How an attached text file is sent to every backend: a header line, then the content in a code fence. The fence is
/// longer than any run of backticks in the content (the markdown rule), so a file holding its own ``` can't close it.
/// <see cref="CliTranscripts"/> strips it again when a chat is reopened.
/// </summary>
public static partial class AttachedFileText
{
    public static string Wrap(string name, string content)
    {
        var fence = new string('`', Math.Max(3, LongestBacktickRun(content) + 1));
        return $"Attached file {name}:\n{fence}\n{content}\n{fence}\n\n";
    }

    /// <summary>Removes every wrapped file from <paramref name="text"/> (line endings already "\n"), handing each name on.</summary>
    public static string Strip(string text, Action<string> name) => Wrapped().Replace(text, m => { name(m.Groups[1].Value); return ""; });

    private static int LongestBacktickRun(string s)
    {
        int longest = 0, run = 0;
        foreach (var c in s)
        {
            run = c == '`' ? run + 1 : 0;
            if (run > longest) longest = run;
        }
        return longest;
    }

    // The closing fence is the opening one (backreference), on its own line.
    [GeneratedRegex(@"Attached file ([^\n]+):\n(`{3,})\n[\s\S]*?\n\2\n\n?")]
    private static partial Regex Wrapped();
}
