using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Tools;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hotline.Core.Backends.Api;

/// <summary>
/// The Anthropic Messages API through the official SDK, streamed. Stateless: the whole conversation (with images)
/// is sent each turn. Default model Claude Opus 5.5 with its default adaptive thinking; effort from the picker.
/// Refusal fallbacks are on by default where supported (a declined request is re-served by a suitable model).
/// </summary>
public sealed class AnthropicBackend(BackendProfile profile, HttpClient http, ISecretStore secrets, Func<BackendProfile, string> systemPrompt,
    FileLog log, IToolHost? tools = null) : IChatBackend
{
    public const string DefaultModel = "claude-opus-5-5";

    /// <summary>Models that accept the server-side refusal fallback ("default" routing).</summary>
    private static readonly HashSet<string> FallbackModels = ["claude-fable-5-1", "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5"];

    public static IReadOnlyList<string> EffortLevels { get; } = ["low", "medium", "high", "xhigh", "max"];

    public string Id => profile.Id;
    public string DisplayName => profile.Name;
    public BackendCapabilities Capabilities { get; } = new(Images: ConnectionTypes.TakesImages(profile), TextFiles: true);

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
        var offered = await ToolLoop.ToolsFor(profile, tools, ct);
        var messages = Messages(conversation);
        for (var round = 0; ; round++)
        {
            var parameters = Build(messages, model, offered);
            log.Debug($"{profile.Id}: Anthropic messages ({messages.Count} messages, {offered.Count} tools, model {model}, effort {profile.Effort ?? "default"})");
            var blocks = new SortedDictionary<long, Block>();
            string? stopReason = null;

            var stream = client.Beta.Messages.CreateStreaming(parameters, ct).GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    BetaRawMessageStreamEvent ev;
                    try
                    {
                        if (!await stream.MoveNextAsync()) break;
                        ev = stream.Current;
                    }
                    catch (Exception ex) when (Map(ex) is { } mapped) { throw mapped; }

                    if (ev.TryPickContentBlockStart(out var start))
                    {
                        var block = new Block();
                        if (start.ContentBlock.TryPickBetaToolUse(out var tu)) { block.Type = "tool_use"; block.Id = tu.ID; block.Name = tu.Name; }
                        else if (start.ContentBlock.TryPickBetaThinking(out var th)) { block.Type = "thinking"; block.Text.Append(th.Thinking); block.Signature = th.Signature; }
                        else if (start.ContentBlock.TryPickBetaRedactedThinking(out var rt)) { block.Type = "redacted_thinking"; block.Data = rt.Data; }
                        else if (start.ContentBlock.TryPickBetaText(out var tx)) { block.Type = "text"; block.Text.Append(tx.Text); }
                        else block.Type = "other";
                        blocks[start.Index] = block;
                        if (block.Type == "text" && block.Text.Length > 0) yield return new ChatDelta(block.Text.ToString()); // usually empty
                    }
                    else if (ev.TryPickContentBlockDelta(out var delta))
                    {
                        blocks.TryGetValue(delta.Index, out var block);
                        if (delta.Delta.TryPickText(out var text) && text.Text.Length > 0)
                        {
                            block?.Text.Append(text.Text);
                            yield return new ChatDelta(text.Text);
                        }
                        else if (delta.Delta.TryPickInputJson(out var json)) block?.Json.Append(json.PartialJson);
                        else if (delta.Delta.TryPickThinking(out var thinking)) block?.Text.Append(thinking.Thinking);
                        else if (delta.Delta.TryPickSignature(out var signature) && block is not null) block.Signature = signature.Signature;
                    }
                    else if (ev.TryPickDelta(out var messageDelta) && messageDelta.Delta.StopReason is { } stop)
                    {
                        stopReason = stop.Raw();
                        if (stopReason == "refusal")
                            throw new BackendException(BackendErrorKind.Failed, $"{ApiCommon.Name(profile)} declined to answer this request (safety refusal).");
                    }
                }
            }
            finally { await stream.DisposeAsync(); }

            var toolUses = blocks.Values.Where(b => b.Type == "tool_use").ToList();
            if (stopReason != "tool_use" || toolUses.Count == 0 || offered.Count == 0) yield break;

            // Send the assistant turn back exactly as streamed (thinking blocks keep their signatures), then the results.
            // Whitespace-only text blocks are dropped: the API rejects them ("text content blocks must contain non-whitespace text").
            messages.Add(new BetaMessageParam
            {
                Role = Role.Assistant,
                Content = blocks.Values.Where(b => b.Type != "other" && !(b.Type == "text" && string.IsNullOrWhiteSpace(b.Text.ToString())))
                    .Select(b => b.ToParam()).ToList(),
            });
            var results = new List<BetaContentBlockParam>();
            foreach (var use in toolUses)
            {
                yield return new ChatDelta(ToolLoop.Note(offered, use.Name));
                var result = await ToolLoop.CallAsync(tools!, offered, use.Name, use.Json.ToString(), ct);
                if (result.IsError) yield return new ChatDelta(ToolLoop.Failed(result));
                results.Add(BetaToolResultBlockParam.FromRawUnchecked(Raw(new JsonObject
                {
                    ["type"] = "tool_result", ["tool_use_id"] = use.Id, ["content"] = ToolLoop.ResultText(result), ["is_error"] = result.IsError,
                })));
            }
            messages.Add(new BetaMessageParam { Role = Role.User, Content = results });
            if (round + 1 >= ToolLoop.MaxRounds)
            {
                yield return new ChatDelta(ToolLoop.RoundLimitNote);
                yield break;
            }
        }
    }

    /// <summary>One streamed content block, kept so the assistant turn can be replayed exactly.</summary>
    private sealed class Block
    {
        public string Type = "";
        public string Id = "";
        public string Name = "";
        public string? Signature;
        public string? Data;
        public readonly StringBuilder Text = new();
        public readonly StringBuilder Json = new();

        /// <summary>The streamed input, or {} if it isn't a JSON object (the API rejects any other tool_use input on replay).</summary>
        public JsonElement Input()
        {
            try
            {
                var input = JsonDocument.Parse(Json.Length > 0 ? Json.ToString() : "{}").RootElement.Clone();
                if (input.ValueKind == JsonValueKind.Object) return input;
            }
            catch (JsonException) { /* fall through */ }
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        public BetaContentBlockParam ToParam() => Type switch
        {
            "tool_use" => BetaToolUseBlockParam.FromRawUnchecked(Raw(new JsonObject
            {
                ["type"] = "tool_use", ["id"] = Id, ["name"] = Name, ["input"] = JsonNode.Parse(Input().GetRawText()),
            })),
            "thinking" => BetaThinkingBlockParam.FromRawUnchecked(Raw(new JsonObject
            {
                ["type"] = "thinking", ["thinking"] = Text.ToString(), ["signature"] = Signature ?? "",
            })),
            "redacted_thinking" => BetaRedactedThinkingBlockParam.FromRawUnchecked(Raw(new JsonObject { ["type"] = "redacted_thinking", ["data"] = Data ?? "" })),
            _ => new BetaTextBlockParam { Text = Text.ToString() },
        };
    }

    private static IReadOnlyDictionary<string, JsonElement> Raw(JsonObject obj) =>
        JsonDocument.Parse(obj.ToJsonString()).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    private static List<BetaMessageParam> Messages(IReadOnlyList<ChatMessage> conversation)
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
        return messages;
    }

    private MessageCreateParams Build(List<BetaMessageParam> messages, string model, IReadOnlyList<ToolSpec> offered)
    {
        var system = systemPrompt(profile);
        var p = new MessageCreateParams
        {
            Model = model,
            MaxTokens = 64000,
            Messages = messages.ToList(),
        };
        if (offered.Count > 0)
            p = p with
            {
                Tools = offered.Select(t => (BetaToolUnion)BetaTool.FromRawUnchecked(Raw(new JsonObject
                {
                    ["name"] = t.ApiName, ["description"] = t.Description, ["input_schema"] = JsonNode.Parse(t.InputSchema.GetRawText()),
                }))).ToList(),
            };
        if (!string.IsNullOrWhiteSpace(system)) p = p with { System = system.Trim() };
        if (profile.Effort?.Trim().ToLowerInvariant() is { } effort && EffortLevels.Contains(effort))
            p = p with { OutputConfig = new BetaOutputConfig { Effort = effort } };
        if (profile.RefusalFallback && FallbackModels.Contains(model))
            p = p with { Betas = ["server-side-fallback-2026-07-01"], Fallbacks = new Default() };
        return p;
    }

    // Whitespace-only text (e.g. a reply cancelled after its first "\n\n") is rejected by the API too.
    private static string Nonempty(string text) => string.IsNullOrWhiteSpace(text) ? "(no text)" : text;

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
