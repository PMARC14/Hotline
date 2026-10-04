using System.Net.Http.Headers;
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
/// Any OpenAI-style /chat/completions server: OpenAI, OpenRouter, Groq, LM Studio, llama.cpp's llama-server, vLLM.
/// Stateless: the whole conversation (with images as data URLs) is sent each turn; the reply streams as SSE. With
/// Tool use on, Hotline's MCP tools are offered as functions; tool calls run through the tool host and loop.
/// </summary>
public sealed class OpenAiBackend(BackendProfile profile, HttpClient http, ISecretStore secrets, Func<BackendProfile, string> systemPrompt,
    FileLog log, IToolHost? tools = null) : IChatBackend
{
    public string Id => profile.Id;
    public string DisplayName => profile.Name;
    public BackendCapabilities Capabilities { get; } = new(Images: true, TextFiles: true);

    private sealed class PendingCall
    {
        public string Id = "";
        public string Name = "";
        public readonly StringBuilder Arguments = new();
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        var endpoint = ApiCommon.Endpoint(profile);
        var key = ApiCommon.Key(profile, secrets);
        var local = profile.Type == BackendType.Local;
        if (string.IsNullOrWhiteSpace(profile.Model) && !local)
            throw new BackendException(BackendErrorKind.NotConfigured, $"Pick a model for {ApiCommon.Name(profile)} (the Model dropdown lists them).");
        ApiCommon.RequireSafeTransport(profile, endpoint, key);

        var offered = await ToolLoop.ToolsFor(profile, tools, ct);
        var messages = Messages(conversation);
        for (var round = 0; ; round++)
        {
            var calls = new SortedDictionary<int, PendingCall>();
            var text = new StringBuilder();
            var done = false;
            string? finish = null;
            using (var request = Request(endpoint, key, messages, offered))
            using (var response = await ApiCommon.SendAsync(http, request, profile, ct))
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await foreach (var data in ApiCommon.SseData(stream, ct))
                {
                    if (data == "[DONE]") { done = true; break; }
                    JsonElement root;
                    try { root = JsonDocument.Parse(data).RootElement; }
                    catch (JsonException) { continue; }
                    if (root.TryGetProperty("error", out _))
                        throw new BackendException(BackendErrorKind.Failed, $"{ApiCommon.Name(profile)}: {ApiCommon.ErrorMessage(data)}");
                    if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) continue;
                    if (choices[0].TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String) finish = fr.GetString();
                    if (!choices[0].TryGetProperty("delta", out var delta)) continue;
                    if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } piece)
                    {
                        text.Append(piece);
                        yield return new ChatDelta(piece);
                    }
                    if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            var index = tc.TryGetProperty("index", out var i) && i.TryGetInt32(out var n) ? n : calls.Count;
                            if (!calls.TryGetValue(index, out var call)) calls[index] = call = new PendingCall();
                            if (tc.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } callId) call.Id = callId;
                            if (tc.TryGetProperty("function", out var fn))
                            {
                                if (fn.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } fnName) call.Name = fnName;
                                if (fn.TryGetProperty("arguments", out var args) && args.GetString() is { } part) call.Arguments.Append(part);
                            }
                        }
                }
            }

            if (calls.Count == 0 || offered.Count == 0) yield break;
            // Never run tool calls from a cut-off stream: their arguments may be incomplete.
            if (finish is "length" or "content_filter")
                throw new BackendException(BackendErrorKind.Failed, $"{ApiCommon.Name(profile)} stopped ({finish}) while asking for a tool; nothing was run.");
            if (!done && finish is null)
                throw new BackendException(BackendErrorKind.Failed, $"{ApiCommon.Name(profile)}: the connection ended before the tool request was complete; nothing was run.");
            // The model asked for tools: record its turn, run each call, feed the results back.
            var toolCallsJson = new JsonArray();
            foreach (var call in calls.Values)
            {
                if (call.Id.Length == 0) call.Id = "call_" + Guid.NewGuid().ToString("N")[..8];
                toolCallsJson.Add(new JsonObject
                {
                    ["id"] = call.Id, ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments.Length > 0 ? call.Arguments.ToString() : "{}" },
                });
            }
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = text.Length > 0 ? text.ToString() : null, ["tool_calls"] = toolCallsJson });
            foreach (var call in calls.Values)
            {
                yield return new ChatDelta(ToolLoop.Note(offered, call.Name));
                var result = await ToolLoop.CallAsync(tools!, offered, call.Name, call.Arguments.ToString(), ct);
                if (result.IsError) yield return new ChatDelta(ToolLoop.Failed(result));
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call.Id, ["content"] = ToolLoop.ResultText(result) });
            }
            if (round + 1 >= ToolLoop.MaxRounds)
            {
                yield return new ChatDelta(ToolLoop.RoundLimitNote);
                yield break;
            }
        }
    }

    private HttpRequestMessage Request(string endpoint, string? key, JsonArray messages, IReadOnlyList<ToolSpec> offered)
    {
        var body = new JsonObject { ["stream"] = true, ["messages"] = messages.DeepClone() };
        if (!string.IsNullOrWhiteSpace(profile.Model)) body["model"] = profile.Model.Trim();
        if (profile.Effort?.Trim().ToLowerInvariant() is "low" or "medium" or "high" && profile.Effort is { } effort)
            body["reasoning_effort"] = effort.Trim().ToLowerInvariant();
        if (offered.Count > 0)
            body["tools"] = new JsonArray(offered.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.ApiName, ["description"] = t.Description, ["parameters"] = JsonNode.Parse(t.InputSchema.GetRawText()),
                },
            }).ToArray());
        log.Debug($"{profile.Id}: POST chat/completions ({messages.Count} messages, {offered.Count} tools, model {profile.Model ?? "server default"})");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (key is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("X-Title", "Hotline"); // OpenRouter app attribution (ignored elsewhere)
        return request;
    }

    private JsonArray Messages(IReadOnlyList<ChatMessage> conversation)
    {
        var messages = new JsonArray();
        var system = systemPrompt(profile);
        if (!string.IsNullOrWhiteSpace(system)) messages.Add(new JsonObject { ["role"] = "system", ["content"] = system.Trim() });
        foreach (var m in ApiCommon.Alternating(conversation))
        {
            var text = ApiCommon.WithTextFiles(m);
            var images = m.Attachments.Where(a => a.Kind == AttachmentKind.Image).ToList();
            if (m.Role == ChatRole.Assistant || images.Count == 0)
            {
                messages.Add(new JsonObject { ["role"] = m.Role == ChatRole.User ? "user" : "assistant", ["content"] = text });
                continue;
            }
            var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text.Length > 0 ? text : "Please look at the attached image(s)." } };
            foreach (var image in images)
                parts.Add(new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = $"data:{image.MimeType};base64,{Convert.ToBase64String(image.Data)}" },
                });
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = parts });
        }
        return messages;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
