using Hotline.Core.Text.Localization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends.Api;

/// <summary>The HTTP half of <see cref="ApiCommon"/>: redirect refusal, capped reads, retries with backoff, error messages.</summary>
public static partial class ApiCommon
{
    /// <summary>Caps on what a server can make us hold in memory: one SSE line or event, an error body, a model list.</summary>
    public const int MaxSseChars = 8 * 1024 * 1024;
    public const int MaxErrorBodyBytes = 256 * 1024;
    public const int MaxModelListBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Where a 3xx answer points, as scheme://host[:port] only (no path, query or credentials, which could carry
    /// secrets), or null when the answer isn't a redirect. Redirects are never followed: the key headers would go along.
    /// </summary>
    public static string? RedirectTarget(HttpResponseMessage response, Uri? requestUri)
    {
        if ((int)response.StatusCode is not (300 or 301 or 302 or 303 or 307 or 308)) return null;
        var location = response.Headers.Location;
        if (location is { IsAbsoluteUri: false } && requestUri is not null && !Uri.TryCreate(requestUri, location, out location)) location = null;
        return location is { IsAbsoluteUri: true } ? $"{location.Scheme}://{location.Authority}" : "another address";
    }

    /// <summary>Reads at most <paramref name="maxBytes"/> of a body (as UTF-8); Truncated says there was more.</summary>
    public static async Task<(string Text, bool Truncated)> ReadCappedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffered = new MemoryStream();
        var chunk = new byte[8192];
        while (buffered.Length <= maxBytes)
        {
            var n = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, maxBytes + 1 - buffered.Length)), ct);
            if (n == 0) break;
            buffered.Write(chunk, 0, n);
        }
        var truncated = buffered.Length > maxBytes;
        return (Encoding.UTF8.GetString(buffered.GetBuffer(), 0, (int)Math.Min(buffered.Length, maxBytes)), truncated);
    }

    /// <summary>Tries per request for 429/503/529 (the first try included).</summary>
    public const int MaxTries = 3;

    /// <summary>A suggested wait longer than this fails at once instead (a popup shouldn't sit for minutes).</summary>
    public static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(60);

    /// <summary>One step of <see cref="SendWithRetriesAsync"/>: a status note while waiting, or the final response.</summary>
    public readonly record struct SendStep(HttpResponseMessage? Response, string? Status);

    /// <summary>
    /// Sends a request (built fresh for each try) and retries rate limits and overload (429/503/529) with the
    /// server's suggested wait or 2 s, 4 s backoff, at most <see cref="MaxTries"/> tries. Yields a status note before
    /// each wait, then the successful response. Retries happen only before a response streams, so nothing repeats.
    /// </summary>
    public static async IAsyncEnumerable<SendStep> SendWithRetriesAsync(HttpClient http, Func<HttpRequestMessage> request, BackendProfile p,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var (response, wait, reason) = await TrySendAsync(http, request(), p, attempt, ct);
            if (response is not null)
            {
                yield return new SendStep(response, null);
                yield break;
            }
            var seconds = (int)Math.Ceiling(wait.TotalSeconds);
            yield return new SendStep(null, L.F(reason == RateLimited ? Strings.Api_RetryRateLimited : Strings.Api_RetryBusy, Name(p), seconds));
            await Task.Delay(wait, ct);
        }
    }

    /// <summary>One try: the response, a wait before retrying, or a BackendException with the server's message.</summary>
    private static async Task<(HttpResponseMessage? Response, TimeSpan Wait, string Reason)> TrySendAsync(HttpClient http, HttpRequestMessage request, BackendProfile p, int attempt, CancellationToken ct)
    {
        using var _ = request; // the response doesn't need it once the headers are in
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException ex)
        {
            throw new BackendException(BackendErrorKind.ServerDown, L.F(Strings.Api_Unreachable, request.RequestUri?.GetLeftPart(UriPartial.Authority), ex.Message), ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new BackendException(BackendErrorKind.ServerDown, L.F(Strings.Api_Timeout, Name(p)), ex);
        }
        if (RedirectTarget(response, request.RequestUri) is { } target)
        {
            response.Dispose();
            throw new BackendException(BackendErrorKind.Failed, L.F(Strings.Api_Redirected, Name(p), target));
        }
        if (response.IsSuccessStatusCode) return (response, default, "");
        var status = response.StatusCode;
        var reasonPhrase = response.ReasonPhrase;
        TimeSpan? wait;
        string body;
        using (response)
        {
            body = "";
            try { body = (await ReadCappedAsync(response.Content, MaxErrorBodyBytes, ct)).Text; } catch (Exception ex) when (ex is not OperationCanceledException) { }
            wait = RetryDelay(response, body, attempt, DateTimeOffset.UtcNow);
        }
        if (wait is { } w) return (null, w, status == HttpStatusCode.TooManyRequests ? RateLimited : "busy");
        var message = ErrorMessage(body) ?? $"{(int)status} {reasonPhrase}";
        throw status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BackendException(BackendErrorKind.Unauthorized, L.F(Strings.Api_KeyRejected, Name(p), message)),
            // Google answers 400 for a bad key
            HttpStatusCode.BadRequest when message.Contains("API key", StringComparison.OrdinalIgnoreCase) => new BackendException(BackendErrorKind.Unauthorized, $"{Name(p)}: {message}"),
            HttpStatusCode.TooManyRequests => new BackendException(BackendErrorKind.RateLimited, L.F(Strings.Api_RateLimited, Name(p), message)),
            >= HttpStatusCode.InternalServerError => new BackendException(BackendErrorKind.ServerDown, L.F(Strings.Api_ServerError, Name(p), message)),
            _ => new BackendException(BackendErrorKind.Failed, $"{Name(p)}: {message}"),
        };
    }

    /// <summary>
    /// How long to wait before trying again, or null when this failure isn't retried: only 429/503/529, only before
    /// try <see cref="MaxTries"/>, never for an exhausted quota, never longer than <see cref="MaxRetryWait"/>. Uses
    /// Retry-After, then Gemini's RetryInfo.retryDelay, then 2 s, 4 s.
    /// </summary>
    public static TimeSpan? RetryDelay(HttpResponseMessage response, string body, int attempt, DateTimeOffset now)
    {
        if ((int)response.StatusCode is not (429 or 503 or 529) || attempt >= MaxTries) return null;
        if (body.Contains("insufficient_quota", StringComparison.Ordinal)) return null;
        TimeSpan? wait = null;
        if (response.Headers.RetryAfter is { } ra)
            wait = ra.Delta ?? (ra.Date is { } date ? date - now : null);
        if (wait is null && RetryDelayInBody().Match(body) is { Success: true } m
            && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s))
            wait = TimeSpan.FromSeconds(s);
        wait ??= TimeSpan.FromSeconds(Math.Pow(2, attempt));
        if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
        return wait > MaxRetryWait ? null : wait;
    }

    [System.Text.RegularExpressions.GeneratedRegex("""
        "retryDelay"\s*:\s*"([0-9.]+)s"
        """)]
    private static partial System.Text.RegularExpressions.Regex RetryDelayInBody();

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
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m))
                    return m.ValueKind == JsonValueKind.String ? m.GetString() : m.GetRawText(); // FastAPI-style servers send a list
            }
            return root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String ? msg.GetString() : null;
        }
        catch (JsonException) { return body.Length is > 0 and < 300 ? body.Trim() : null; }
    }
}
