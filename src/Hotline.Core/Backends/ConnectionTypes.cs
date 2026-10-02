using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

[Flags]
public enum ConnectionField { None = 0, Endpoint = 1, ApiKey = 2, CliPath = 4, Agent = 8, ExtraArgs = 16, Tools = 32 }

public sealed record ConnectionTypeInfo(
    BackendType Type, string DisplayName, string DefaultName, ConnectionField Fields, string? DefaultEndpoint,
    IReadOnlyList<string> EffortLevels, bool ApiKeyOptional, string Description, bool EffortInModelId = false)
{
    public bool Has(ConnectionField field) => (Fields & field) == field;
}

/// <summary>What each kind of AI connection needs. Effort levels are listed only where verified (agy).</summary>
public static class ConnectionTypes
{
    private const ConnectionField Cli = ConnectionField.CliPath | ConnectionField.ExtraArgs | ConnectionField.Tools;
    private const ConnectionField Api = ConnectionField.Endpoint | ConnectionField.ApiKey;

    public static IReadOnlyList<ConnectionTypeInfo> All { get; } =
    [
        new(BackendType.Antigravity, "Antigravity CLI (agy)", "Gemini (Antigravity)", Cli | ConnectionField.Agent, null,
            ["low", "medium", "high"], false, "Your installed agy and its Google sign-in.", EffortInModelId: true),
        new(BackendType.ClaudeCode, "Claude Code CLI", "Claude (Claude Code)", Cli, null,
            ClaudeCode.ClaudeCodeProtocol.EffortLevels, false, "Your installed claude and its sign-in (your Claude plan)."),
        new(BackendType.Gemini, "Gemini API", "Gemini API", Api, "https://generativelanguage.googleapis.com/v1beta",
            [], false, "A Google AI Studio API key."),
        new(BackendType.Anthropic, "Anthropic API", "Claude API", Api, "https://api.anthropic.com/v1",
            Hotline.Core.Backends.Api.AnthropicBackend.EffortLevels, false, "An Anthropic Console API key. Default model: Claude Opus 5.5."),
        new(BackendType.OpenAiCompatible, "OpenAI-compatible API", "OpenAI-compatible", Api, "https://api.openai.com/v1",
            [], false, "OpenAI, OpenRouter, Groq and other OpenAI-style APIs."),
        new(BackendType.Local, "Local endpoint", "Local model", Api, "http://127.0.0.1:8080/v1",
            [], true, "llama.cpp server, LM Studio and other local OpenAI-style servers."),
    ];

    public static ConnectionTypeInfo Of(BackendType type) => All.First(t => t.Type == type);
}
