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
        if (p.Tools == ToolMode.Inherit)
        {
            // The user's own Claude Code setup: settings, permissions, MCP servers, hooks, tools.
            if (p.ApproveAllTools) args.Add("--dangerously-skip-permissions");
        }
        else
        {
            // Chat only: no tools, and none of the user's settings/plugins/hooks/MCP servers. (Not --bare: it never
            // reads the user's Claude login.)
            args.AddRange(["--tools", "", "--setting-sources", "", "--strict-mcp-config"]);
        }
        if (!string.IsNullOrWhiteSpace(p.Model)) args.AddRange(["--model", p.Model.Trim()]);
        if (p.Effort?.Trim().ToLowerInvariant() is { } effort && EffortLevels.Contains(effort)) args.AddRange(["--effort", effort]);
        if (!string.IsNullOrWhiteSpace(systemPrompt)) args.AddRange(["--append-system-prompt", systemPrompt.Trim()]);
        if (!string.IsNullOrWhiteSpace(p.ExtraArgs))
        {
            var extra = p.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < extra.Length; i++)
            {
                var a = extra[i];
                if (a.Contains("dangerous", StringComparison.OrdinalIgnoreCase) || a.Contains("skip-permission", StringComparison.OrdinalIgnoreCase)) continue;
                if (a.Equals("--permission-mode", StringComparison.OrdinalIgnoreCase)) { i++; continue; } // and its value
                args.Add(a);
            }
        }
        return args;
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
            sb.AppendLine("Conversation so far (for context):");
            foreach (var m in priorContext)
                sb.Append(m.Role == ChatRole.User ? "User: " : "Assistant: ").AppendLine(m.Text);
            sb.AppendLine().AppendLine("New message:");
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
