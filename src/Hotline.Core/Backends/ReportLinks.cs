using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

/// <summary>Where to report an inappropriate answer: the provider's own page (none of them has an API for it).</summary>
public sealed record ReportTarget(string Provider, Uri Page);

public static class ReportLinks
{
    /// <summary>Anthropic's "Reporting harmful or illegal content" help article (it also gives usersafety@anthropic.com).</summary>
    public static readonly Uri Anthropic = new("https://support.claude.com/en/articles/7996906-reporting-blocking-and-removing-content-from-claude");

    /// <summary>Google has no general form for Gemini answers; its Generative AI Prohibited Use Policy links its reporting options.</summary>
    public static readonly Uri Google = new("https://support.google.com/gemini/answer/16625148");

    /// <summary>Null for OpenAI-compatible and local connections: no single provider to report to (yet).</summary>
    public static ReportTarget? For(BackendType type) => type switch
    {
        BackendType.ClaudeCode or BackendType.Anthropic => new("Anthropic", Anthropic),
        BackendType.Antigravity or BackendType.Gemini => new("Google", Google),
        _ => null,
    };
}
