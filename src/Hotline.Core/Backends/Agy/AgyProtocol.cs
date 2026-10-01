using System.Text;
using System.Text.Json;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

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
        IReadOnlyList<ChatMessage> priorContext)
    {
        var sb = new StringBuilder();
        if (priorContext.Count > 0)
        {
            sb.AppendLine("Conversation so far (for context):");
            foreach (var m in priorContext)
                sb.Append(m.Role == ChatRole.User ? "User: " : "Assistant: ").AppendLine(m.Text);
            sb.AppendLine().AppendLine("New message:");
        }
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

    public static IReadOnlyList<string> BuildArgs(BackendProfile p)
    {
        var args = new List<string>
        {
            "--input-format", "stream-json", "--output-format", "stream-json", "-p=",
            "--agent", string.IsNullOrWhiteSpace(p.Agent) ? DefaultAgent : p.Agent,
        };
        if (!string.IsNullOrWhiteSpace(p.Model)) args.AddRange(["--model", p.Model]);
        if (p.Effort?.Trim().ToLowerInvariant() is "low" or "medium" or "high" or "max")
            args.AddRange(["--effort", p.Effort.Trim().ToLowerInvariant()]);
        if (!string.IsNullOrWhiteSpace(p.ExtraArgs))
            args.AddRange(p.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(a => !a.Equals("--dangerously-skip-permissions", StringComparison.OrdinalIgnoreCase)));
        return args;
    }
}

public static class AgyLocator
{
    public static string? Find(string? configuredPath, Func<string, bool> exists, string? localAppData, string? pathEnv)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
            return exists(expanded) ? expanded : null;
        }
        if (!string.IsNullOrEmpty(localAppData))
        {
            var installed = Path.Combine(localAppData, "agy", "bin", "agy.exe");
            if (exists(installed)) return installed;
        }
        foreach (var dir in (pathEnv ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "agy.exe");
            if (exists(candidate)) return candidate;
        }
        return null;
    }
}

public static class AgyErrors
{
    public static BackendException Map(string error)
    {
        bool Has(params string[] words) => words.Any(w => error.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (Has("unauthenticated", "sign in", "signed in", "log in", "logged in", "login"))
            return new(BackendErrorKind.NotLoggedIn, "agy isn't signed in. Run `agy` once in a terminal to sign in, then try again.");
        if (Has("resource_exhausted", "quota", "rate limit"))
            return new(BackendErrorKind.RateLimited, "Your Antigravity quota is used up for now. Try again later.");
        if (Has("not trusted", "trust"))
            return new(BackendErrorKind.NotConfigured, "agy doesn't trust Hotline's workspace yet. Run `agy` once in a terminal and trust it.");
        if (Has("wasn't allowed", "denied"))
            return new(BackendErrorKind.Unsupported, error);
        return new(BackendErrorKind.Failed, $"agy: {error}");
    }
}
