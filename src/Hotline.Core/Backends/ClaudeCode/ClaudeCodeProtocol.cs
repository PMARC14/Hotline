using System.Text;
using System.Text.Json;
using Hotline.Core.Chat;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;

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
        var chatOnly = p.Tools == ToolMode.ChatOnly;
        if (p.Args is not null) AddSafe(args, p.Args, chatOnly); // the user's own launch flags replace the mode defaults
        if (!string.IsNullOrWhiteSpace(p.Model)) args.AddRange(["--model", p.Model.Trim()]);
        if (p.Effort?.Trim().ToLowerInvariant() is { } effort && EffortLevels.Contains(effort)) args.AddRange(["--effort", effort]);
        if (!string.IsNullOrWhiteSpace(systemPrompt)) args.AddRange(["--append-system-prompt", systemPrompt.Trim()]);
        if (!string.IsNullOrWhiteSpace(p.ExtraArgs)) AddSafe(args, p.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries), chatOnly);
        if (chatOnly)
        {
            // Chat only: no tools, and none of the user's settings/plugins/hooks/MCP servers. (Not --bare: it never
            // reads the user's Claude login.) Last and whatever "args" say: a copied connection file can't widen it.
            args.AddRange(["--tools", "", "--setting-sources", "", "--strict-mcp-config"]);
        }
        return args;
    }

    /// <summary>
    /// Flags that let tools run without asking, or run commands outright (hooks in settings, agents and plugins). Only
    /// ApproveAllTools may loosen permissions.
    /// </summary>
    private static readonly string[] Blocked =
    [
        "--allowedTools", "--allowed-tools", "--permission-mode", "--permission-prompts", "--permission-prompt-tool",
        "--settings", "--agents", "--plugin-dir", "--plugin-url", "--yolo", "--trust",
    ];

    /// <summary>Chat only's own isolation flags; the user's copies would replace (or, being lists, add to) them.</summary>
    private static readonly string[] ChatOnlyOwned = ["--tools", "--setting-sources", "--mcp-config", "--strict-mcp-config", "--agent"];

    /// <summary>User-supplied flags, minus anything that would bypass permissions or, in chat only, the isolation.</summary>
    private static void AddSafe(List<string> args, IReadOnlyList<string> extra, bool chatOnly)
    {
        for (var i = 0; i < extra.Count; i++)
        {
            var a = extra[i];
            if (a.Contains("dangerous", StringComparison.OrdinalIgnoreCase) || a.Contains("skip-permission", StringComparison.OrdinalIgnoreCase)) continue;
            var name = a.Split('=', 2)[0];
            if (Blocked.Contains(name, StringComparer.OrdinalIgnoreCase) || chatOnly && ChatOnlyOwned.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                // And its separate values: several of these take lists ("--allowedTools Bash Edit"), and a stray value
                // would be read as the prompt.
                if (!a.Contains('='))
                    while (i + 1 < extra.Count && !extra[i + 1].StartsWith('-')) i++;
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
            return new(BackendErrorKind.NotLoggedIn, Strings.Claude_NotSignedIn);
        if (Has("usage limit", "rate limit", "rate_limit", "429"))
            return new(BackendErrorKind.RateLimited, L.F(Strings.Claude_Limit, error.Split('|')[0].Trim()));
        if (Has("overloaded", "529", "service unavailable"))
            return new(BackendErrorKind.ServerDown, Strings.Claude_Overloaded);
        return new(BackendErrorKind.Failed, error);
    }
}
