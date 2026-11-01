namespace Hotline.Core.Processes;

/// <summary>
/// What Hotline hands to the shell (the user's editor, Explorer): only its own text files and folders, so a path that
/// came from a file or a name can't open a program, a web page or something outside them.
/// </summary>
public static class ShellOpen
{
    public static IReadOnlyList<string> EditorExtensions { get; } = [".md", ".json", ".txt"];

    /// <summary>The full path of an existing .md/.json/.txt file inside one of <paramref name="roots"/>, else null.</summary>
    public static string? EditableFile(string path, IReadOnlyList<string> roots, Func<string, bool> fileExists)
    {
        if (Inside(path, roots) is not { } full) return null;
        if (!EditorExtensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase)) return null;
        return fileExists(full) ? full : null;
    }

    /// <summary>The full path of a folder inside (or one of) <paramref name="roots"/>, else null.</summary>
    /// Never ends in '\' and never contains '"', so it can be quoted whole for Explorer (which splits unquoted commas).
    public static string? OpenableFolder(string path, IReadOnlyList<string> roots) => Inside(path, roots)?.TrimEnd('\\');

    private static string? Inside(string path, IReadOnlyList<string> roots)
    {
        // Fully qualified local paths only; no quotes or wildcards, and no ':' past the drive (alternate data streams).
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.IndexOf(':', 2) >= 0 || path.Any(c => c < ' ' || c is '"' or '<' or '>' or '|' or '*' or '?'))
            return null;
        var full = Path.GetFullPath(path);
        foreach (var root in roots)
        {
            var r = Path.GetFullPath(root).TrimEnd('\\');
            if (full.TrimEnd('\\').Equals(r, StringComparison.OrdinalIgnoreCase) || full.StartsWith(r + '\\', StringComparison.OrdinalIgnoreCase))
                return full;
        }
        return null;
    }
}
