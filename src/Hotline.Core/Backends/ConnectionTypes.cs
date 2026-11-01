using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Backends;

[Flags]
public enum ConnectionField { None = 0, Endpoint = 1, ApiKey = 2, CliPath = 4, Agent = 8, ExtraArgs = 16, Tools = 32, MaxTokens = 64, Temperature = 128 }

/// <param name="DisplayNameKey">Resource key of the type's name (shown as <see cref="DisplayName"/>).</param>
/// <param name="DefaultName">A new connection's name (saved in its file, so not translated).</param>
/// <param name="DescriptionKey">Resource key of the one-line description (shown as <see cref="Description"/>).</param>
public sealed record ConnectionTypeInfo(
    BackendType Type, string DisplayNameKey, string DefaultName, ConnectionField Fields, string? DefaultEndpoint,
    IReadOnlyList<string> EffortLevels, bool ApiKeyOptional, string DescriptionKey, bool EffortInModelId = false, bool ImagesByDefault = true,
    bool ListsModels = true)
{
    public string DisplayName => L.Get(DisplayNameKey);
    public string Description => L.Get(DescriptionKey);
    public bool Has(ConnectionField field) => (Fields & field) == field;
}

/// <summary>What each kind of AI connection needs. Effort levels are the ones each API documents (Gemini: thinking level; OpenAI: reasoning_effort).</summary>
public static class ConnectionTypes
{
    private const ConnectionField Cli = ConnectionField.CliPath | ConnectionField.ExtraArgs | ConnectionField.Tools;
    private const ConnectionField Api = ConnectionField.Endpoint | ConnectionField.ApiKey | ConnectionField.Tools | ConnectionField.MaxTokens;

    public static IReadOnlyList<ConnectionTypeInfo> All { get; } =
    [
        new(BackendType.Antigravity, nameof(Strings.Type_Agy), "Gemini (Antigravity)", Cli | ConnectionField.Agent, null,
            ["low", "medium", "high"], false, nameof(Strings.Type_Agy_Desc), EffortInModelId: true),
        new(BackendType.ClaudeCode, nameof(Strings.Type_ClaudeCode), "Claude (Claude Code)", Cli, null,
            ClaudeCode.ClaudeCodeProtocol.EffortLevels, false, nameof(Strings.Type_ClaudeCode_Desc)),
        new(BackendType.Codex, nameof(Strings.Type_Codex), "ChatGPT (Codex)", Cli, null,
            Codex.CodexProtocol.EffortLevels, false, nameof(Strings.Type_Codex_Desc)),
        new(BackendType.Gemini, nameof(Strings.Type_Gemini), "Gemini API", Api | ConnectionField.Temperature, "https://generativelanguage.googleapis.com/v1beta",
            ["low", "medium", "high"], false, nameof(Strings.Type_Gemini_Desc)),
        new(BackendType.Anthropic, nameof(Strings.Type_Anthropic), "Claude API", Api, "https://api.anthropic.com/v1",
            Hotline.Core.Backends.Api.AnthropicBackend.EffortLevels, false, nameof(Strings.Type_Anthropic_Desc)),
        new(BackendType.OpenAiCompatible, nameof(Strings.Type_OpenAi), "OpenAI-compatible", Api | ConnectionField.Temperature, "https://api.openai.com/v1",
            ["low", "medium", "high"], false, nameof(Strings.Type_OpenAi_Desc)),
        // Local servers (llama.cpp, LM Studio, vLLM) take reasoning_effort for reasoning models; it's only sent when chosen.
        new(BackendType.Local, nameof(Strings.Type_Local), "Local model", Api | ConnectionField.Temperature, "http://127.0.0.1:8080/v1",
            ["low", "medium", "high"], true, nameof(Strings.Type_Local_Desc), ImagesByDefault: false),
    ];

    /// <summary>The type's info. A hand-edited file can hold a number that is no type (it can't be used: BackendFactory.IsAvailable); it reads as OpenAI-compatible instead of throwing.</summary>
    public static ConnectionTypeInfo Of(BackendType type) =>
        All.FirstOrDefault(t => t.Type == type) ?? All.First(t => t.Type == BackendType.OpenAiCompatible);

    /// <summary>A connection that runs the user's installed CLI (Antigravity, Claude Code, Codex).</summary>
    public static bool IsCli(BackendType type) => All.FirstOrDefault(t => t.Type == type)?.Has(ConnectionField.CliPath) == true;

    /// <summary>
    /// Whether Hotline keeps a chat's messages in its own history: always for API and local connections; for a CLI only
    /// when the CLI doesn't keep it (keepCliSessions off) or the user also wants it in Hotline (keepInHotline).
    /// </summary>
    public static bool HotlineKeepsTranscript(BackendProfile p) => !IsCli(p.Type) || !p.KeepCliSessions || p.KeepInHotline;

    /// <summary>Whether this connection's model reads images: its "images" setting, else the type's default.</summary>
    public static bool TakesImages(BackendProfile p) => p.Images ?? Of(p.Type).ImagesByDefault;
}
