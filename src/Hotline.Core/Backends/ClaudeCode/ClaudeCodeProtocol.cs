using System.Text;
using System.Text.Json;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends.ClaudeCode;

/// <summary>
/// The Claude Code CLI in print mode with stream-json in and out (claude 2.1.x). One persistent process per
/// conversation; each user turn is one JSON line on stdin.
/// </summary>
public static class ClaudeCodeProtocol
{
    public static IReadOnlyList<string> EffortLevels { get; } = ["low", "medium", "high", "xhigh", "max"];

    public static IReadOnlyList<string> BuildArgs(BackendProfile p, string systemPrompt)
    {
        var args = new List<string>
        {
            "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
            // In the background nobody can answer a permission prompt: anything that would ask is denied.
            "--permission-prompts", "none",
        };
        if (!p.KeepCliSessions) args.Add("--no-session-persistence"); // Hotline keeps its own history
        if (p.Tools == ToolMode.Inherit && p.ApproveAllTools) args.Add("--dangerously-skip-permissions");
        if (p.Args is not null) AddSafe(args, p.Args); // the user's own launch flags replace the mode defaults
        else if (p.Tools == ToolMode.ChatOnly)
        {
            // Chat only: no tools, and none of the user's settings/plugins/hooks/MCP servers. (Not --bare: it never
            // reads the user's Claude login.)
            args.AddRange(["--tools", "", "--setting-sources", "", "--strict-mcp-config"]);
        }
        if (!string.IsNullOrWhiteSpace(p.Model)) args.AddRange(["--model", p.Model.Trim()]);
        if (p.Effort?.Trim().ToLowerInvariant() is { } effort && EffortLevels.Contains(effort)) args.AddRange(["--effort", effort]);
        if (!string.IsNullOrWhiteSpace(systemPrompt)) args.AddRange(["--append-system-prompt", systemPrompt.Trim()]);
        if (!string.IsNullOrWhiteSpace(p.ExtraArgs)) AddSafe(args, p.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return args;
    }

    /// <summary>User-supplied flags, minus anything that would bypass permissions (only ApproveAllTools may).</summary>
    private static void AddSafe(List<string> args, IReadOnlyList<string> extra)
    {
        for (var i = 0; i < extra.Count; i++)
        {
            var a = extra[i];
            if (a.Contains("dangerous", StringComparison.OrdinalIgnoreCase) || a.Contains("skip-permission", StringComparison.OrdinalIgnoreCase)) continue;
            if (a.StartsWith("--permission-mode", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--permission-prompts", StringComparison.OrdinalIgnoreCase))
            {
                if (!a.Contains('=')) i++; // and its separate value
                continue;
            }
            args.Add(a);
        }
    }

    /// <summary>One user turn: text (with replayed context when the session is fresh) plus inline images.</summary>
    public static string UserLine(string text, IReadOnlyList<Attachment> images)
    {
        var content = new List<object> { new { type = "text", text } };
        foreach (var image in images)
            content.Add(new { type = "image", source = new { type = "base64", media_type = image.MimeType, data = Convert.ToBase64String(image.Data) } });
        return JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content } });
    }

    public static string ComposeText(string text, IReadOnlyList<(string Name, string Content)> textFiles, IReadOnlyList<ChatMessage> priorContext)
    {
        var sb = new StringBuilder();
        if (priorContext.Count > 0)
        {
            sb.Append("<previous_conversation>\n");
            foreach (var m in priorContext)
                sb.Append("<turn role=\"").Append(m.Role == ChatRole.User ? "user" : "assistant").Append("\">\n").Append(m.Text).Append("\n</turn>\n");
            sb.Append("</previous_conversation>\n\nNew message:\n");
        }
        foreach (var (name, content) in textFiles)
            sb.Append("Attached file ").Append(name).AppendLine(":").AppendLine("```").AppendLine(content).AppendLine("```").AppendLine();
        sb.Append(text.Length > 0 ? text : "Please look at the attachment(s).");
        return sb.ToString();
    }
}

public static class ClaudeLocator
{
    /// <summary>Configured path, else the native installer's %USERPROFILE%\.local\bin\claude.exe, else PATH.</summary>
    public static string? Find(string? configuredPath, Func<string, bool> exists, string? userProfile, string? pathEnv)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
            return exists(expanded) ? expanded : null;
        }
        if (!string.IsNullOrEmpty(userProfile))
        {
            var native = Path.Combine(userProfile, ".local", "bin", "claude.exe");
            if (exists(native)) return native;
        }
        foreach (var dir in (pathEnv ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "claude.exe");
            if (exists(candidate)) return candidate;
        }
        return null;
    }
}

public static class ClaudeErrors
{
    public static BackendException Map(string error)
    {
        bool Has(params string[] words) => words.Any(w => error.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (Has("/login", "not logged in", "invalid api key", "oauth token", "authentication"))
            return new(BackendErrorKind.NotLoggedIn, "Claude Code isn't signed in. Run `claude` once in a terminal and sign in, then try again.");
        if (Has("usage limit", "rate limit", "rate_limit", "429"))
            return new(BackendErrorKind.RateLimited, "Claude usage limit reached for now. " + error.Split('|')[0].Trim());
        if (Has("overloaded", "529", "service unavailable"))
            return new(BackendErrorKind.ServerDown, "Claude is overloaded right now; try again in a moment.");
        return new(BackendErrorKind.Failed, error);
    }
}
