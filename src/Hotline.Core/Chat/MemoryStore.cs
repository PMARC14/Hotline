using System.Text.RegularExpressions;

namespace Hotline.Core.Chat;

/// <summary>
/// ~/.hotline/memory.md: notes every chat sees (added to the system prompt of every connection). Written by the user
/// (edit the file, or "/remember …") or, with approval, by the model's remember tool. HTML comments are not memory.
/// </summary>
public sealed partial class MemoryStore(string path)
{
    /// <summary>Longest single fact /remember or the tool may add (keeps the system prompt small).</summary>
    public const int MaxFactChars = 500;

    private static readonly Lock Gate = new();

    private const string Header =
        "<!-- Hotline memory: every chat sees the text below (it's added to the system prompt). Edit it freely;\n" +
        "     \"/remember something\" in the message box adds a line, and the AI can add one when you allow it. -->\n";

    public string Path => path;

    public void EnsureFile()
    {
        lock (Gate)
        {
            if (File.Exists(path)) return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Header);
        }
    }

    /// <summary>The memory text (comments removed, trimmed); empty when there's none or the file can't be read.</summary>
    public string Read()
    {
        try
        {
            if (!File.Exists(path)) return "";
            return Comments().Replace(File.ReadAllText(path), "").ReplaceLineEndings("\n").Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>Adds "- fact" as a new line. False for an empty fact or one over <see cref="MaxFactChars"/>.</summary>
    public bool Append(string fact)
    {
        var line = LineBreaks().Replace(fact.Trim(), " ");
        if (line.Length == 0 || line.Length > MaxFactChars) return false;
        lock (Gate)
        {
            EnsureFileUnlocked();
            var existing = File.ReadAllText(path);
            var prefix = existing.Length == 0 || existing.EndsWith('\n') ? "" : "\n";
            File.AppendAllText(path, $"{prefix}- {line}\n");
        }
        return true;
    }

    private void EnsureFileUnlocked()
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Header);
    }

    /// <summary>The system prompt with the memory appended (unchanged when there's no memory).</summary>
    public static string Compose(string systemPrompt, string memory) => memory.Length == 0
        ? systemPrompt
        : systemPrompt.TrimEnd() + "\n\n## What the user asked you to remember\n" + memory;

    /// <summary>"/remember fact" typed in the message box; <paramref name="fact"/> is empty for a bare "/remember".</summary>
    public static bool TryParseCommand(string text, out string fact)
    {
        var m = Command().Match(text.Trim());
        fact = m.Success ? m.Groups[1].Value.Trim() : "";
        return m.Success;
    }

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"\s*[\r\n]+\s*")]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"^/remember(?:\s+(.*))?\z", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Command();
}
