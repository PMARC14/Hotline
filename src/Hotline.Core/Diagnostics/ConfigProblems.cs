namespace Hotline.Core.Diagnostics;

/// <summary>
/// How a hand-edited file's problems reach the user (a notice in the panel, a line in Settings): short, one line each,
/// cleaned like a log line, and only announced again when the set of problems changes.
/// </summary>
public static class ConfigProblems
{
    private const int MaxShown = 3;
    private const int MaxLength = 160;

    /// <summary>The first problems as one sentence part: "a; b; c (and 2 more)".</summary>
    public static string Summary(IReadOnlyList<string> problems)
    {
        var shown = string.Join("; ", problems.Take(MaxShown).Select(One));
        return problems.Count > MaxShown ? $"{shown} (and {problems.Count - MaxShown} more)" : shown;
    }

    /// <summary>Changes when the problems change; empty when there are none (so the notice can be taken away).</summary>
    public static string Key(string? error, IReadOnlyList<string> problems) =>
        error is null && problems.Count == 0 ? "" : $"{error}\n{string.Join("\n", problems)}";

    private static string One(string problem)
    {
        var text = LogText.Clean(problem).Replace("\n    ", " ").Replace('\n', ' ');
        return text.Length > MaxLength ? text[..(MaxLength - 1)] + "…" : text;
    }
}
