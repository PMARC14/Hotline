using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends.Api;

/// <summary>Shared plumbing for the HTTP API backends: endpoint, key, transport safety, SSE and error mapping.</summary>
public static class ApiCommon
{
    public static string Endpoint(BackendProfile p) =>
        (string.IsNullOrWhiteSpace(p.Endpoint) ? ConnectionTypes.Of(p.Type).DefaultEndpoint! : p.Endpoint.Trim()).TrimEnd('/');

    /// <summary>The API key, or a NotConfigured error when the connection needs one and has none.</summary>
    public static string? Key(BackendProfile p, ISecretStore secrets)
    {
        var key = secrets.Get(SecretKeys.ApiKey(p.Id));
        if (string.IsNullOrEmpty(key) && !ConnectionTypes.Of(p.Type).ApiKeyOptional)
            throw new BackendException(BackendErrorKind.NotConfigured, $"Add an API key for {Name(p)} in Settings › AI connections.");
        return string.IsNullOrEmpty(key) ? null : key;
    }

    /// <summary>A key only travels over https, or plain http to this PC (local servers).</summary>
    public static void RequireSafeTransport(BackendProfile p, string endpoint, string? key)
    {
        if (key is null) return;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new BackendException(BackendErrorKind.NotConfigured, $"The endpoint for {Name(p)} isn't a valid http(s) address: {endpoint}");
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new BackendException(BackendErrorKind.NotConfigured,
                $"Not sending the API key for {Name(p)} over plain http to {uri.Host}; use an https:// endpoint.");
    }

    public static string Name(BackendProfile p) => string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name;

    /// <summary>Sends the request; maps network failures and HTTP errors to BackendExceptions with the server's message.</summary>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request, BackendProfile p, CancellationToken ct)
    {
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException ex)
        {
            throw new BackendException(BackendErrorKind.ServerDown, $"Can't reach {request.RequestUri?.GetLeftPart(UriPartial.Authority)}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new BackendException(BackendErrorKind.ServerDown, $"{Name(p)} didn't answer in time.", ex);
        }
        if (response.IsSuccessStatusCode) return response;
        var body = "";
        try { body = await response.Content.ReadAsStringAsync(ct); } catch (Exception) { }
        var message = ErrorMessage(body) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}";
        response.Dispose();
        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BackendException(BackendErrorKind.Unauthorized, $"{Name(p)} rejected the API key. {message}"),
            HttpStatusCode.TooManyRequests => new BackendException(BackendErrorKind.RateLimited, $"{Name(p)} rate limit or quota reached. {message}"),
            >= HttpStatusCode.InternalServerError => new BackendException(BackendErrorKind.ServerDown, $"{Name(p)} had a server error. {message}"),
            _ => new BackendException(BackendErrorKind.Failed, $"{Name(p)}: {message}"),
        };
    }

    /// <summary>The human message from a JSON error body ({"error":{"message"}} / {"error":"..."} / {"message"}).</summary>
    public static string? ErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0]; // Gemini sometimes wraps in an array
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) return error.GetString();
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m)) return m.GetString();
            }
            return root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String ? msg.GetString() : null;
        }
        catch (JsonException) { return body.Length is > 0 and < 300 ? body.Trim() : null; }
    }

    /// <summary>Server-sent events: yields each event's data (multi-line data joined with \n).</summary>
    public static async IAsyncEnumerable<string> SseData(Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0) { yield return data.ToString(); data.Clear(); }
                continue;
            }
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                var value = line.AsSpan(5);
                data.Append(value.StartsWith(" ") ? value[1..] : value); // the spec strips one space: keep indentation
            }
        }
        if (data.Length > 0) yield return data.ToString();
    }

    /// <summary>
    /// Merges consecutive messages of the same role (e.g. a question whose answer was cancelled, then the next
    /// question): Anthropic and Gemini require alternating turns.
    /// </summary>
    public static IReadOnlyList<ChatMessage> Alternating(IReadOnlyList<ChatMessage> conversation)
    {
        var result = new List<ChatMessage>();
        foreach (var m in conversation)
        {
            if (result.Count > 0 && result[^1].Role == m.Role)
            {
                var last = result[^1];
                var text = string.Join("\n\n", new[] { last.Text, m.Text }.Where(t => t.Length > 0));
                result[^1] = last with { Text = text, Attachments = [.. last.Attachments, .. m.Attachments] };
            }
            else result.Add(m);
        }
        return result;
    }

    public static string WithTextFiles(ChatMessage m)
    {
        var files = m.Attachments.Where(a => a.Kind == AttachmentKind.Text).ToList();
        if (files.Count == 0) return m.Text;
        var sb = new StringBuilder();
        foreach (var f in files) sb.Append("Attached file ").Append(f.Name).Append(":\n```\n").Append(f.AsText()).Append("\n```\n\n");
        sb.Append(m.Text.Length > 0 ? m.Text : "Please look at the attachment(s).");
        return sb.ToString();
    }
}
