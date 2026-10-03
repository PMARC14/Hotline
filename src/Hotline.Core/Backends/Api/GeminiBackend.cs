using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends.Api;

/// <summary>
/// The Gemini API (Google AI Studio key): models/{model}:streamGenerateContent with server-sent events. Stateless;
/// the whole conversation (with inline images) is sent each turn. Thought parts are not shown.
/// </summary>
public sealed class GeminiBackend(BackendProfile profile, HttpClient http, ISecretStore secrets, Func<BackendProfile, string> systemPrompt, FileLog log) : IChatBackend
{
    public string Id => profile.Id;
    public string DisplayName => profile.Name;
    public BackendCapabilities Capabilities { get; } = new(Images: true, TextFiles: true);

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        var endpoint = ApiCommon.Endpoint(profile);
        if (string.IsNullOrWhiteSpace(profile.Model))
            throw new BackendException(BackendErrorKind.NotConfigured, $"Pick a model for {ApiCommon.Name(profile)} (the Model dropdown lists them).");
        var key = ApiCommon.Key(profile, secrets);
        ApiCommon.RequireSafeTransport(profile, endpoint, key);
        var model = profile.Model.Trim();
        if (model.StartsWith("models/", StringComparison.Ordinal)) model = model[7..];

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse")
        {
            Content = new StringContent(Body(conversation), Encoding.UTF8, "application/json"),
        };
        if (key is not null) request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
        log.Debug($"{profile.Id}: Gemini streamGenerateContent ({conversation.Count} messages, model {model})");

        HttpResponseMessage response;
        try { response = await ApiCommon.SendAsync(http, request, profile, ct); }
        catch (BackendException ex) when (ex.Kind == BackendErrorKind.Failed && ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase))
        {
            throw new BackendException(BackendErrorKind.Unauthorized, ex.Message, ex); // Google answers 400 for a bad key
        }
        using (response)
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
                        if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                        if (part.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } piece) yield return new ChatDelta(piece);
                    }
                if (candidate.TryGetProperty("finishReason", out var finish) && finish.GetString() is "SAFETY" or "RECITATION" or "PROHIBITED_CONTENT" or "BLOCKLIST")
                    throw new BackendException(BackendErrorKind.Failed, $"{ApiCommon.Name(profile)} stopped the answer ({finish.GetString()}).");
            }
        }
    }

    private string Body(IReadOnlyList<ChatMessage> conversation)
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
        var body = new JsonObject { ["contents"] = contents };
        var system = systemPrompt(profile);
        if (!string.IsNullOrWhiteSpace(system))
            body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = system.Trim() } } };
        return body.ToJsonString();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
