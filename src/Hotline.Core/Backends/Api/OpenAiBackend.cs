using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends.Api;

/// <summary>
/// Any OpenAI-style /chat/completions server: OpenAI, OpenRouter, Groq, LM Studio, llama.cpp's llama-server, vLLM.
/// Stateless: the whole conversation (with images as data URLs) is sent each turn; the reply streams as SSE.
/// </summary>
public sealed class OpenAiBackend(BackendProfile profile, HttpClient http, ISecretStore secrets, Func<BackendProfile, string> systemPrompt, FileLog log) : IChatBackend
{
    public string Id => profile.Id;
    public string DisplayName => profile.Name;
    public BackendCapabilities Capabilities { get; } = new(Images: true, TextFiles: true);

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        var endpoint = ApiCommon.Endpoint(profile);
        var key = ApiCommon.Key(profile, secrets);
        var local = profile.Type == BackendType.Local;
        if (string.IsNullOrWhiteSpace(profile.Model) && !local)
            throw new BackendException(BackendErrorKind.NotConfigured, $"Pick a model for {ApiCommon.Name(profile)} (the Model dropdown lists them).");
        ApiCommon.RequireSafeTransport(profile, endpoint, key);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions")
        {
            Content = new StringContent(Body(conversation), Encoding.UTF8, "application/json"),
        };
        if (key is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("X-Title", "Hotline"); // OpenRouter app attribution (ignored elsewhere)

        using var response = await ApiCommon.SendAsync(http, request, profile, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await foreach (var data in ApiCommon.SseData(stream, ct))
        {
            if (data == "[DONE]") yield break;
            JsonElement root;
            try { root = JsonDocument.Parse(data).RootElement; }
            catch (JsonException) { continue; }
            if (root.TryGetProperty("error", out _))
                throw new BackendException(BackendErrorKind.Failed, $"{ApiCommon.Name(profile)}: {ApiCommon.ErrorMessage(data)}");
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) continue;
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } piece)
                yield return new ChatDelta(piece);
        }
    }

    private string Body(IReadOnlyList<ChatMessage> conversation)
    {
        var messages = new JsonArray();
        var system = systemPrompt(profile);
        if (!string.IsNullOrWhiteSpace(system)) messages.Add(new JsonObject { ["role"] = "system", ["content"] = system.Trim() });
        foreach (var m in conversation)
        {
            var text = ApiCommon.WithTextFiles(m);
            var images = m.Attachments.Where(a => a.Kind == AttachmentKind.Image).ToList();
            if (m.Role == ChatRole.Assistant || images.Count == 0)
            {
                messages.Add(new JsonObject { ["role"] = m.Role == ChatRole.User ? "user" : "assistant", ["content"] = text });
                continue;
            }
            var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } };
            foreach (var image in images)
                parts.Add(new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = $"data:{image.MimeType};base64,{Convert.ToBase64String(image.Data)}" },
                });
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = parts });
        }
        var body = new JsonObject { ["stream"] = true, ["messages"] = messages };
        if (!string.IsNullOrWhiteSpace(profile.Model)) body["model"] = profile.Model.Trim();
        if (profile.Effort?.Trim().ToLowerInvariant() is "low" or "medium" or "high" && profile.Effort is { } effort)
            body["reasoning_effort"] = effort.Trim().ToLowerInvariant();
        log.Debug($"{profile.Id}: POST chat/completions ({conversation.Count} messages, model {profile.Model ?? "server default"})");
        return body.ToJsonString();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
