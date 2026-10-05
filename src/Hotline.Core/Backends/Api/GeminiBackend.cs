using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Tools;

namespace Hotline.Core.Backends.Api;

/// <summary>
/// The Gemini API (Google AI Studio key): models/{model}:streamGenerateContent with server-sent events. Stateless;
/// the whole conversation (with inline images) is sent each turn. Thought parts are not shown. With Tool use on,
/// Hotline's MCP tools are offered as function declarations; the model's parts are echoed back unchanged (they may
/// carry thought signatures) followed by the function responses.
/// </summary>
public sealed class GeminiBackend(BackendProfile profile, HttpClient http, ISecretStore secrets, Func<BackendProfile, string> systemPrompt,
    FileLog log, IToolHost? tools = null) : IChatBackend
{
    public string Id => profile.Id;
    public string DisplayName => profile.Name;
    public BackendCapabilities Capabilities { get; } = new(Images: ConnectionTypes.TakesImages(profile), TextFiles: true);

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        var endpoint = ApiCommon.Endpoint(profile);
        if (string.IsNullOrWhiteSpace(profile.Model))
            throw new BackendException(BackendErrorKind.NotConfigured, $"Pick a model for {ApiCommon.Name(profile)} (the Model dropdown lists them).");
        var key = ApiCommon.Key(profile, secrets);
        ApiCommon.RequireSafeTransport(profile, endpoint, key);
        var model = profile.Model.Trim();
        if (model.StartsWith("models/", StringComparison.Ordinal)) model = model[7..];

        var offered = await ToolLoop.ToolsFor(profile, tools, ct);
        var contents = Contents(conversation);
        for (var round = 0; ; round++)
        {
            var modelParts = new JsonArray();
            var calls = new List<(string Name, string? Id, JsonElement Args)>();
            HttpResponseMessage? sent = null;
            await foreach (var step in ApiCommon.SendWithRetriesAsync(http, () => Request(endpoint, model, key, contents, offered), profile, ct))
                if (step.Status is { } note) yield return ChatDelta.StatusNote(note);
                else sent = step.Response;
            using (var response = sent!)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await foreach (var data in ApiCommon.SseData(stream, ct))
                {
                    JsonElement root;
                    try { root = JsonDocument.Parse(data).RootElement; }
                    catch (JsonException) { continue; }
                    if (root.TryGetProperty("error", out _))
                        throw new BackendException(BackendErrorKind.Failed, $"{ApiCommon.Name(profile)}: {ApiCommon.ErrorMessage(data)}");
                    if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var reason))
                        throw new BackendException(BackendErrorKind.Failed, $"{ApiCommon.Name(profile)} blocked this request ({reason.GetString()}).");
                    if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0) continue;
                    var candidate = candidates[0];
                    if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                        foreach (var part in parts.EnumerateArray())
                        {
                            modelParts.Add(JsonNode.Parse(part.GetRawText()));
                            if (part.TryGetProperty("functionCall", out var fc))
                            {
                                var name = fc.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                                var id = fc.TryGetProperty("id", out var i) ? i.GetString() : null;
                                var args = fc.TryGetProperty("args", out var a) ? a.Clone() : JsonDocument.Parse("{}").RootElement;
                                calls.Add((name, id, args));
                                continue;
                            }
                            if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                            if (part.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } piece) yield return new ChatDelta(piece);
                        }
                    if (candidate.TryGetProperty("finishReason", out var finish) && finish.GetString() is "SAFETY" or "RECITATION" or "PROHIBITED_CONTENT" or "BLOCKLIST")
                        throw new BackendException(BackendErrorKind.Failed, $"{ApiCommon.Name(profile)} stopped the answer ({finish.GetString()}).");
                }
            }

            if (calls.Count == 0 || offered.Count == 0) yield break;
            contents.Add(new JsonObject { ["role"] = "model", ["parts"] = modelParts });
            var responses = new JsonArray();
            foreach (var (name, id, args) in calls)
            {
                yield return new ChatDelta(ToolLoop.Note(offered, name));
                var result = await ToolLoop.CallAsync(tools!, offered, name, args.GetRawText(), ct);
                if (result.IsError) yield return new ChatDelta(ToolLoop.Failed(result));
                var fr = new JsonObject
                {
                    ["name"] = name,
                    ["response"] = result.IsError ? new JsonObject { ["error"] = ToolLoop.ResultText(result) } : new JsonObject { ["content"] = ToolLoop.ResultText(result) },
                };
                if (id is not null) fr["id"] = id;
                responses.Add(new JsonObject { ["functionResponse"] = fr });
            }
            contents.Add(new JsonObject { ["role"] = "user", ["parts"] = responses });
            if (round + 1 >= ToolLoop.MaxRounds)
            {
                yield return new ChatDelta(ToolLoop.RoundLimitNote);
                yield break;
            }
        }
    }

    private HttpRequestMessage Request(string endpoint, string model, string? key, JsonArray contents, IReadOnlyList<ToolSpec> offered)
    {
        var body = new JsonObject { ["contents"] = contents.DeepClone() };
        var system = systemPrompt(profile);
        if (!string.IsNullOrWhiteSpace(system))
            body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = system.Trim() } } };
        if (offered.Count > 0)
            body["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["functionDeclarations"] = new JsonArray(offered.Select(t => (JsonNode)new JsonObject
                    {
                        // parametersJsonSchema takes plain JSON Schema (MCP schemas use keys the older "parameters" rejects)
                        ["name"] = t.ApiName, ["description"] = t.Description, ["parametersJsonSchema"] = JsonNode.Parse(t.InputSchema.GetRawText()),
                    }).ToArray()),
                },
            };
        if (ThinkingConfig(model, profile.Effort) is { } thinking) body["generationConfig"] = new JsonObject { ["thinkingConfig"] = thinking };
        log.Debug($"{profile.Id}: Gemini streamGenerateContent ({contents.Count} contents, {offered.Count} tools, model {model}, effort {profile.Effort ?? "default"})");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (key is not null) request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
        return request;
    }

    /// <summary>
    /// Effort → generationConfig.thinkingConfig: Gemini 3+ takes thinkingLevel (low/medium/high); Gemini 2.x only a
    /// thinkingBudget in tokens; 1.x has no thinking. Unknown or unset effort sends nothing (the model's default).
    /// </summary>
    private static JsonObject? ThinkingConfig(string model, string? effort)
    {
        var level = effort?.Trim().ToLowerInvariant();
        if (level is not ("low" or "medium" or "high")) return null;
        if (model.StartsWith("gemini-1", StringComparison.OrdinalIgnoreCase)) return null; // no thinking
        if (model.StartsWith("gemini-2", StringComparison.OrdinalIgnoreCase))
            return new JsonObject { ["thinkingBudget"] = level switch { "low" => 1024, "medium" => 8192, _ => 24576 } };
        return new JsonObject { ["thinkingLevel"] = level };
    }

    private static JsonArray Contents(IReadOnlyList<ChatMessage> conversation)
    {
        var contents = new JsonArray();
        foreach (var m in ApiCommon.Alternating(conversation))
        {
            var parts = new JsonArray { new JsonObject { ["text"] = ApiCommon.WithTextFiles(m) is { Length: > 0 } t ? t : "(no text)" } };
            if (m.Role == ChatRole.User)
                foreach (var image in m.Attachments.Where(a => a.Kind == AttachmentKind.Image))
                    parts.Add(new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = image.MimeType, ["data"] = Convert.ToBase64String(image.Data) } });
            contents.Add(new JsonObject { ["role"] = m.Role == ChatRole.User ? "user" : "model", ["parts"] = parts });
        }
        return contents;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
