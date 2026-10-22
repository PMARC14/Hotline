using Hotline.Core.Text.Localization;

namespace Hotline.Core.Platform;

/// <summary>
/// Is a program (an MCP server's command such as npx or uvx) on this PC? Looked up the way cmd.exe does — the given
/// path, else the working directory, else PATH, trying PATHEXT extensions — so a missing one can be reported clearly
/// ("install Node.js") instead of as cmd's "is not recognized" after the fact.
/// </summary>
public static class CommandResolver
{
    private static readonly Dictionary<string, string> Installers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["npx"] = "Node.js (nodejs.org)", ["npm"] = "Node.js (nodejs.org)", ["node"] = "Node.js (nodejs.org)",
        ["uvx"] = "uv (docs.astral.sh/uv)", ["uv"] = "uv (docs.astral.sh/uv)",
        ["python"] = "Python (python.org or the Microsoft Store)", ["py"] = "Python (python.org)", ["pip"] = "Python (python.org)",
        ["docker"] = "Docker Desktop (docker.com)", ["deno"] = "Deno (deno.com)", ["bun"] = "Bun (bun.sh)",
    };

    /// <summary>The full path that would run, or null when the command isn't found.</summary>
    public static string? Find(string command, string? workingDirectory, string? pathEnv, string? pathExt, Func<string, bool> fileExists)
    {
        command = command.Trim().Trim('"');
        if (command.Length == 0) return null;
        var extensions = (string.IsNullOrWhiteSpace(pathExt) ? ".COM;.EXE;.BAT;.CMD" : pathExt)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? Try(string candidate)
        {
            if (!SafeDirectory.IsLocal(candidate)) return null; // touching a share would send the user's NTLM hash
            if (Path.HasExtension(candidate) && fileExists(candidate)) return candidate;
            foreach (var ext in extensions)
                if (fileExists(candidate + ext.ToLowerInvariant())) return candidate + ext.ToLowerInvariant(); // Windows paths ignore case
            return null;
        }
        if (Path.IsPathFullyQualified(command)) return Try(command);
        var hasDirectory = command.Contains('\\') || command.Contains('/');
        if (hasDirectory || workingDirectory is not null)
        {
            var baseDir = workingDirectory ?? Environment.CurrentDirectory;
            string full;
            try { full = Path.GetFullPath(Path.Combine(baseDir, command)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return null; } // NUL, a too long or malformed path
            if (Try(full) is { } local) return local;
            if (hasDirectory) return null;
        }
        foreach (var dir in (pathEnv ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try { candidate = Path.Combine(dir.Trim('"'), command); }
            catch (ArgumentException) { continue; } // a malformed PATH entry
            if (Try(candidate) is { } found) return found;
        }
        return null;
    }

    /// <summary>What to tell the user when a server's command isn't on this PC.</summary>
    public static string MissingMessage(string server, string command)
    {
        var name = Path.GetFileNameWithoutExtension(command.Trim().Trim('"'));
        return Installers.TryGetValue(name, out var install)
            ? L.F(Strings.Mcp_MissingKnown, name, server, install)
            : L.F(Strings.Mcp_Missing, command, server);
    }
}
