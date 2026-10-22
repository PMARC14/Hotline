using System.Text;
using System.Text.Json;
using Hotline.Core.Chat;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Backends.Agy;

/// <summary>Antigravity CLI (agy 1.2.x) stream-json protocol helpers. Verified against agy 1.2.14 (2026-10-02).</summary>
public static class AgyProtocol
{
    public const string DefaultAgent = "hotline";

    /// <summary>One NDJSON input line. agy accepts only text content.</summary>
    public static string UserLine(string prompt) =>
        JsonSerializer.Serialize(new { @event = "user", message = new { role = "user", content = prompt } });

    public static string ComposePrompt(
        string text, IReadOnlyList<string> imagePaths, IReadOnlyList<(string Name, string Content)> textFiles,
        IReadOnlyList<ChatMessage> priorContext, string? instructions = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(instructions))
            sb.AppendLine("Instructions for this conversation:").AppendLine(instructions.Trim()).AppendLine();
        if (priorContext.Count > 0)
        {
            sb.AppendLine("Conversation so far (for context):");
            foreach (var m in priorContext)
                sb.Append(m.Role == ChatRole.User ? "User: " : "Assistant: ").AppendLine(m.Text);
            sb.AppendLine().AppendLine("New message:");
        }
        // Instructions span paragraphs (memory adds its own): the marker tells where the user's message starts.
        else if (!string.IsNullOrWhiteSpace(instructions)) sb.AppendLine("New message:");
        if (imagePaths.Count > 0)
        {
            sb.AppendLine("Attached images (in the workspace; use view_file to look at them):");
            foreach (var p in imagePaths) sb.Append("- ").AppendLine(p);
            sb.AppendLine();
        }
        foreach (var (name, content) in textFiles)
            sb.Append("Attached file ").Append(name).AppendLine(":").AppendLine("```").AppendLine(content).AppendLine("```").AppendLine();
        sb.Append(text.Length > 0 ? text : "Please look at the attached file(s).");
        return sb.ToString();
    }

    public static IReadOnlyList<string> BuildArgs(BackendProfile p, string? addDir = null)
    {
        var args = new List<string> { "--input-format", "stream-json", "--output-format", "stream-json", "-p=" };
        var chatOnly = p.Tools == ToolMode.ChatOnly;
        // Chat only: the hotline agent, whatever "args" say (a copied connection file can't pick another). Before the
        // user's flags: agy stops reading flags at the first stray word, and their own --agent is dropped anyway.
        if (chatOnly) args.AddRange(["--agent", string.IsNullOrWhiteSpace(p.Agent) ? DefaultAgent : p.Agent]);
        if (p.Args is not null) AddSafe(args, p.Args, chatOnly); // replaces the agent flags (in Inherit mode)
        if (!string.IsNullOrWhiteSpace(addDir)) args.AddRange(["--add-dir", addDir]);
        // Only via the explicit, warned setting (never from ExtraArgs) and never in chat-only mode.
        if (p.Tools == ToolMode.Inherit && p.ApproveAllTools) args.Add("--dangerously-skip-permissions");
        if (!string.IsNullOrWhiteSpace(p.Model)) args.AddRange(["--model", p.Model]);
        if (p.Effort?.Trim().ToLowerInvariant() is "low" or "medium" or "high" or "max")
            args.AddRange(["--effort", p.Effort.Trim().ToLowerInvariant()]);
        if (!string.IsNullOrWhiteSpace(p.ExtraArgs)) AddSafe(args, p.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries), chatOnly);
        return args;
    }

    /// <summary>
    /// User-supplied flags, minus anything that approves tools without asking (only ApproveAllTools may) and, in chat
    /// only, the agent and folder flags Hotline sets. agy reads "-flag" like "--flag".
    /// </summary>
    private static void AddSafe(List<string> args, IReadOnlyList<string> extra, bool chatOnly)
    {
        for (var i = 0; i < extra.Count; i++)
        {
            var a = extra[i];
            if (a.Contains("dangerous", StringComparison.OrdinalIgnoreCase) || a.Contains("skip-permission", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = a.TrimStart('-').Split('=', 2);
            var name = a.StartsWith('-') ? parts[0].ToLowerInvariant() : "";
            var inline = parts.Length > 1 ? parts[1] : null;
            if (name is "yolo" or "trust" or "remote-control") continue; // switches: no separate value
            var value = inline ?? (i + 1 < extra.Count ? extra[i + 1] : null);
            // auto / accept-edits approve without asking; plan only plans.
            var drop = name == "mode" && !string.Equals(value, "plan", StringComparison.OrdinalIgnoreCase)
                       || chatOnly && name is "agent" or "add-dir";
            if (drop)
            {
                if (inline is null) i++; // and its separate value
                continue;
            }
            args.Add(a);
        }
    }
}

public static class AgyErrors
{
    public static BackendException Map(string error)
    {
        bool Has(params string[] words) => words.Any(w => error.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (Has("unauthenticated", "sign in", "signed in", "log in", "logged in", "login"))
            return new(BackendErrorKind.NotLoggedIn, Strings.Agy_NotSignedIn);
        if (Has("resource_exhausted", "quota", "rate limit"))
            return new(BackendErrorKind.RateLimited, Strings.Agy_Quota);
        if (Has("not trusted", "trust"))
            return new(BackendErrorKind.NotConfigured, Strings.Agy_NotTrusted);
        if (Has("wasn't allowed", "denied"))
            return new(BackendErrorKind.Unsupported, error);
        return new(BackendErrorKind.Failed, $"agy: {error}");
    }
}
