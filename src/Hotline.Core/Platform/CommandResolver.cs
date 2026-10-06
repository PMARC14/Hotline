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
            if (Try(Path.GetFullPath(Path.Combine(baseDir, command))) is { } local) return local;
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
            ? $"\"{name}\" isn't installed, so the {server} server can't start. Install {install}, then restart Hotline."
            : $"\"{command}\" isn't installed or isn't on PATH, so the {server} server can't start. Check \"command\" in mcp.json.";
    }
}
