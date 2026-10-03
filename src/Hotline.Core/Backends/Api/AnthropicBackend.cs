using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends.Api;

/// <summary>
/// The Anthropic Messages API through the official SDK, streamed. Stateless: the whole conversation (with images)
/// is sent each turn. Default model Claude Opus 5.5 with its default adaptive thinking; effort from the picker.
/// Refusal fallbacks are on by default where supported (a declined request is re-served by a suitable model).
/// </summary>
public sealed class AnthropicBackend(BackendProfile profile, HttpClient http, ISecretStore secrets, Func<BackendProfile, string> systemPrompt, FileLog log) : IChatBackend
{
    public const string DefaultModel = "claude-opus-5-5";

    /// <summary>Models that accept the server-side refusal fallback ("default" routing).</summary>
    private static readonly HashSet<string> FallbackModels = ["claude-fable-5-1", "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5"];

    public static IReadOnlyList<string> EffortLevels { get; } = ["low", "medium", "high", "xhigh", "max"];

    public string Id => profile.Id;
    public string DisplayName => profile.Name;
    public BackendCapabilities Capabilities { get; } = new(Images: true, TextFiles: true);

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        var endpoint = ApiCommon.Endpoint(profile);
        var key = ApiCommon.Key(profile, secrets)!;
        ApiCommon.RequireSafeTransport(profile, endpoint, key);
        var model = string.IsNullOrWhiteSpace(profile.Model) ? DefaultModel : profile.Model.Trim();

        var client = new AnthropicClient
        {
            ApiKey = key,
            BaseUrl = endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? endpoint[..^3] : endpoint,
            HttpClient = http,
        };
        var parameters = Build(conversation, model);
        log.Debug($"{profile.Id}: Anthropic messages ({conversation.Count} messages, model {model}, effort {profile.Effort ?? "default"})");

        var stream = client.Beta.Messages.CreateStreaming(parameters, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                BetaRawMessageStreamEvent ev;
                try
                {
                    if (!await stream.MoveNextAsync()) yield break;
                    ev = stream.Current;
                }
                catch (Exception ex) when (Map(ex) is { } mapped) { throw mapped; }

                if (ev.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text) && text.Text.Length > 0)
                    yield return new ChatDelta(text.Text);
                else if (ev.TryPickDelta(out var messageDelta) && messageDelta.Delta.StopReason is { } stop && stop.Raw() == "refusal")
                    throw new BackendException(BackendErrorKind.Failed, $"{ApiCommon.Name(profile)} declined to answer this request (safety refusal).");
            }
        }
        finally { await stream.DisposeAsync(); }
    }

    private MessageCreateParams Build(IReadOnlyList<ChatMessage> conversation, string model)
    {
        var messages = new List<BetaMessageParam>();
        foreach (var m in ApiCommon.Alternating(conversation))
        {
            var blocks = new List<BetaContentBlockParam> { new BetaTextBlockParam { Text = Nonempty(ApiCommon.WithTextFiles(m)) } };
            if (m.Role == ChatRole.User)
                foreach (var image in m.Attachments.Where(a => a.Kind == AttachmentKind.Image))
                    blocks.Add(new BetaImageBlockParam
                    {
                        Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(image.Data), MediaType = image.MimeType },
                    });
            messages.Add(new BetaMessageParam { Role = m.Role == ChatRole.User ? Role.User : Role.Assistant, Content = blocks });
        }
        var system = systemPrompt(profile);
        var p = new MessageCreateParams
        {
            Model = model,
            MaxTokens = 64000,
            Messages = messages,
        };
        if (!string.IsNullOrWhiteSpace(system)) p = p with { System = system.Trim() };
        if (profile.Effort?.Trim().ToLowerInvariant() is { } effort && EffortLevels.Contains(effort))
            p = p with { OutputConfig = new BetaOutputConfig { Effort = effort } };
        if (profile.RefusalFallback && FallbackModels.Contains(model))
            p = p with { Betas = ["server-side-fallback-2026-07-01"], Fallbacks = new Default() };
        return p;
    }

    private static string Nonempty(string text) => text.Length > 0 ? text : "(no text)";

    private BackendException? Map(Exception ex) => ex switch
    {
        BackendException => null,
        OperationCanceledException => null,
        AnthropicUnauthorizedException or AnthropicForbiddenException
            => new(BackendErrorKind.Unauthorized, $"{ApiCommon.Name(profile)} rejected the API key. {ex.Message}", ex),
        AnthropicRateLimitException => new(BackendErrorKind.RateLimited, $"{ApiCommon.Name(profile)} rate limit or quota reached. {ex.Message}", ex),
        Anthropic5xxException => new(BackendErrorKind.ServerDown, $"{ApiCommon.Name(profile)} had a server error (or is overloaded). {ex.Message}", ex),
        AnthropicIOException => new(BackendErrorKind.ServerDown, $"Can't reach {ApiCommon.Endpoint(profile)}: {ex.Message}", ex),
        AnthropicApiException => new(BackendErrorKind.Failed, $"{ApiCommon.Name(profile)}: {ex.Message}", ex),
        _ => null,
    };

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
