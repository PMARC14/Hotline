using System.Text;
using Hotline.Core.Chat;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Backends.Codex;

/// <summary>
/// The OpenAI Codex CLI, non-interactive: one `codex exec --json` process per turn, prompt on stdin. Chat only is
/// a read-only sandbox in an empty folder without the user's config.toml or rules (unless the connection's own "args"
/// replace those defaults); Inherit is the user's own setup
/// with a workspace-write sandbox. Exec never asks for approval, so anything outside the sandbox is refused.
/// </summary>
public static class CodexProtocol
{
    public static IReadOnlyList<string> EffortLevels { get; } = ["low", "medium", "high", "xhigh", "max", "ultra"];

    /// <param name="resumeThread">Thread id to continue (only when the user keeps Codex sessions), else a new session.</param>
    /// <param name="imagePaths">Image files to attach (codex takes paths, not data).</param>
    public static IReadOnlyList<string> BuildArgs(BackendProfile p, string systemPrompt, string? resumeThread, IReadOnlyList<string> imagePaths)
    {
        var args = new List<string> { "exec", "--json", "--skip-git-repo-check", "--color", "never" };
        if (!p.KeepCliSessions) args.Add("--ephemeral"); // Hotline keeps its own history
        var inherit = p.Tools == ToolMode.Inherit;
        if (inherit && p.ApproveAllTools) args.Add("--dangerously-bypass-approvals-and-sandbox");
        else args.AddRange(["--sandbox", inherit ? "workspace-write" : "read-only"]);
        args.AddRange(["-c", "approval_policy=\"never\""]); // nobody can answer a prompt in the background
        if (p.Args is not null) AddSafe(args, p.Args, chatOnly: !inherit); // the user's own launch flags replace the mode defaults
        if (!string.IsNullOrWhiteSpace(p.Model)) args.AddRange(["--model", p.Model.Trim()]);
        if (p.Effort?.Trim().ToLowerInvariant() is { } effort && EffortLevels.Contains(effort))
            args.AddRange(["-c", $"model_reasoning_effort={Toml(effort)}"]);
        if (!string.IsNullOrWhiteSpace(systemPrompt)) args.AddRange(["-c", $"developer_instructions={Toml(systemPrompt.Trim())}"]);
        if (!string.IsNullOrWhiteSpace(p.ExtraArgs)) AddSafe(args, p.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries), chatOnly: !inherit);
        // Chat only: never the user's config.toml (MCP servers, profiles) or rules, whatever "args" say. Before "resume",
        // whose own options these aren't.
        if (!inherit) args.AddRange(["--ignore-user-config", "--ignore-rules"]);
        if (resumeThread is not null) args.AddRange(["resume", resumeThread]);
        foreach (var image in imagePaths) args.AddRange(["--image", image]);
        args.Add("-"); // read the prompt from stdin
        return args;
    }

    /// <summary>
    /// A TOML basic string (-c parses its value as TOML): quotes, backslashes, control characters and DEL escaped;
    /// everything else, emoji included, written as is (TOML has no surrogate-pair escapes).
    /// </summary>
    public static string Toml(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when c < 0x20 || c == 0x7F => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        return sb.Append('"').ToString();
    }

    /// <summary>
    /// User-supplied flags, minus anything that would loosen the sandbox or approvals (only ApproveAllTools may) and, in
    /// chat only, the config Hotline keeps out (profiles, MCP servers, its own --ignore flags).
    /// </summary>
    private static void AddSafe(List<string> args, IReadOnlyList<string> extra, bool chatOnly)
    {
        static bool Loosens(string a) =>
            a.Contains("dangerous", StringComparison.OrdinalIgnoreCase) || a.Contains("full-access", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--yolo", StringComparison.OrdinalIgnoreCase) || a.Equals("--full-auto", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--trust", StringComparison.OrdinalIgnoreCase);
        bool Setting(string a)
        {
            var key = a.TrimStart(' ', '"', '\'', '[', '\t'); // also a quoted key or a table header
            return key.StartsWith("sandbox", StringComparison.OrdinalIgnoreCase) || key.StartsWith("approval", StringComparison.OrdinalIgnoreCase)
                   || chatOnly && key.StartsWith("mcp_servers", StringComparison.OrdinalIgnoreCase);
        }
        for (var i = 0; i < extra.Count; i++)
        {
            var a = extra[i];
            if (Loosens(a)) continue;
            // Added once by Hotline in chat only (a repeated switch is an error).
            if (chatOnly && a is "--ignore-user-config" or "--ignore-rules") continue;
            // "-sX" / "-cX" (value attached to a short flag) are the same as "-s X" / "-c X".
            var attached = a.Length > 2 && a[0] == '-' && a[1] is 's' or 'a' or 'c' or 'C' or 'p' && a[2] != '-' && !a.StartsWith("--", StringComparison.Ordinal);
            var name = attached ? a[..2] : a.Split('=', 2)[0];
            var inline = attached ? a[2..].TrimStart('=') : a.Contains('=') ? a.Split('=', 2)[1] : null;
            if (name is "-s" or "--sandbox" or "-a" or "--ask-for-approval" or "-C" or "--cd" or "--add-dir"
                || chatOnly && name is "-p" or "--profile") // a config.toml profile: chat only ignores the user's config
            {
                // Hotline sets the sandbox, approvals and folder; a different folder would escape the empty chat folder.
                if (inline is null) i++; // and its separate value
                continue;
            }
            if (name is "-c" or "--config")
            {
                var value = inline ?? (i + 1 < extra.Count ? extra[i + 1] : "");
                if (Setting(value)) { if (inline is null) i++; continue; }
            }
            args.Add(a);
        }
    }

    /// <summary>The text sent on stdin: replayed context when the session is fresh, attached text files, the message.</summary>
    public static string ComposeText(string text, IReadOnlyList<(string Name, string Content)> textFiles, IReadOnlyList<ChatMessage> priorContext) =>
        ClaudeCode.ClaudeCodeProtocol.ComposeText(text, textFiles, priorContext);
}

public static class CodexLocator
{
    /// <summary>
    /// Configured path, else codex.exe on PATH, else the native binary inside an npm global install
    /// (`npm i -g @openai/codex` puts only a node shim on PATH; Hotline starts the real exe directly).
    /// </summary>
    /// <param name="arch">"x64" or "arm64" (the machine's, not the app's).</param>
    public static string? Find(string? configuredPath, Func<string, bool> exists, string? pathEnv, string arch)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
            return exists(expanded) ? expanded : null;
        }
        var dirs = (pathEnv ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).ToList();
        foreach (var dir in dirs)
        {
            var candidate = Path.Combine(dir, "codex.exe");
            if (exists(candidate)) return candidate;
        }
        var (triple, package) = arch == "arm64" ? ("aarch64-pc-windows-msvc", "codex-win32-arm64") : ("x86_64-pc-windows-msvc", "codex-win32-x64");
        var binary = Path.Combine(triple, "bin", "codex.exe");
        foreach (var dir in dirs)
        {
            var codex = Path.Combine(dir, "node_modules", "@openai", "codex");
            foreach (var candidate in new[]
            {
                Path.Combine(codex, "node_modules", "@openai", package, "vendor", binary),
                Path.Combine(dir, "node_modules", "@openai", package, "vendor", binary),
                Path.Combine(codex, "vendor", binary),
            })
                if (exists(candidate)) return candidate;
        }
        return null;
    }
}

public static class CodexErrors
{
    public static BackendException Map(string error)
    {
        bool Has(params string[] words) => words.Any(w => error.Contains(w, StringComparison.OrdinalIgnoreCase));
        // Status codes only as whole numbers: request ids and timings contain digits too.
        bool Code(params string[] codes) => codes.Any(c => System.Text.RegularExpressions.Regex.IsMatch(error, $@"(?<![\w.]){c}(?![\w.])"));
        if (Has("usage limit", "rate limit", "rate_limit", "quota") || Code("429"))
            return new(BackendErrorKind.RateLimited, L.F(Strings.Codex_Limit, error.Trim()));
        if (Has("not logged in", "codex login", "unauthorized", "invalid api key", "authentication") || Code("401"))
            return new(BackendErrorKind.NotLoggedIn, Strings.Codex_NotSignedIn);
        if (Has("overloaded", "service unavailable", "server_error") || Code("503"))
            return new(BackendErrorKind.ServerDown, Strings.Codex_Overloaded);
        return new(BackendErrorKind.Failed, error);
    }
}
