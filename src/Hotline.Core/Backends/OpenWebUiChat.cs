using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Hotline.Core.Chat;

namespace Hotline.Core.Backends;

public enum OpenWebUiError { KeyRefused, Unreachable, InsecureKey, BadReply }

public sealed class OpenWebUiException(OpenWebUiError error, string message, Exception? inner = null) : Exception(message, inner)
{
    public OpenWebUiError Error { get; } = error;
}

/// <summary>
/// Continue in › Open WebUI: creates a real chat holding the whole conversation (<c>POST /api/v1/chats/new</c> with the
/// user's API key) and returns its page, <c>{base}/c/{id}</c>. Open WebUI keeps a message tree, so every message gets an
/// id and is chained to the one before (parentId / childrenIds); attachments go by name, as in the handoff file.
/// </summary>
public static class OpenWebUiChat
{
    public static JsonObject Body(string title, string? model, IReadOnlyList<ChatMessage> messages, Func<Guid> newId)
    {
        var ids = messages.Select(_ => newId().ToString()).ToList();
        var list = new JsonArray();
        var byId = new JsonObject();
        for (var i = 0; i < messages.Count; i++)
        {
            var m = messages[i];
            var content = m.Text.TrimEnd() + string.Concat(m.Attachments.Select(a => $"\n[attached: {a.Name}]"));
            JsonObject Node() => new()
            {
                ["id"] = ids[i],
                ["parentId"] = i == 0 ? null : ids[i - 1],
                ["childrenIds"] = i + 1 < ids.Count ? new JsonArray(ids[i + 1]) : new JsonArray(),
                ["role"] = m.Role == ChatRole.User ? "user" : "assistant",
                ["content"] = content,
                ["timestamp"] = m.At.ToUnixTimeSeconds(),
                ["models"] = model is null ? new JsonArray() : new JsonArray(model),
            };
            list.Add(Node());
            byId[ids[i]] = Node();
        }
        return new JsonObject
        {
            ["chat"] = new JsonObject
            {
                ["title"] = title,
                ["models"] = model is null ? new JsonArray() : new JsonArray(model),
                ["messages"] = list,
                ["history"] = new JsonObject { ["messages"] = byId, ["currentId"] = ids.Count > 0 ? ids[^1] : null },
            },
        };
    }

    /// <summary>Creates the chat; the key only goes over https or to this PC (as Hotline's other API keys).</summary>
    public static async Task<Uri> CreateAsync(HttpClient http, Uri baseUrl, string apiKey, JsonObject body, CancellationToken ct)
    {
        if (baseUrl.Scheme != Uri.UriSchemeHttps && !baseUrl.IsLoopback)
            throw new OpenWebUiException(OpenWebUiError.InsecureKey, $"The API key is only sent over https or to this PC, not to {baseUrl}.");
        var root = baseUrl.GetLeftPart(UriPartial.Path).TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, root + "/api/v1/chats/new")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (HttpRequestException ex) { throw new OpenWebUiException(OpenWebUiError.Unreachable, $"Open WebUI isn't running at {root}.", ex); }
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new OpenWebUiException(OpenWebUiError.KeyRefused, "Open WebUI refused the API key.");
            var text = await response.Content.ReadAsStringAsync(ct);
            string? id = null;
            if (response.IsSuccessStatusCode)
                try { id = JsonNode.Parse(text) is JsonObject o ? (string?)o["id"] : null; } // a bare value isn't a chat
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { }
            if (string.IsNullOrEmpty(id))
                throw new OpenWebUiException(OpenWebUiError.BadReply, $"Open WebUI didn't create the chat ({(int)response.StatusCode}).");
            return new Uri($"{root}/c/{Uri.EscapeDataString(id)}");
        }
    }
}
