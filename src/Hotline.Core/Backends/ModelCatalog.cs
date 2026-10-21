using System.Collections.Concurrent;
using System.Text.Json;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Backends;

/// <param name="Efforts">The reasoning efforts the model supports, when the source says (Codex).</param>
public sealed record ModelInfo(string Id, string Label, IReadOnlyList<string>? Efforts = null);

public sealed class ModelListException(string message) : Exception(message);

public static class ModelListParsers
{
    public static IReadOnlyList<ModelInfo> ClaudeCodeAliases { get; } =
        [new("fable", "Fable"), new("opus", "Opus"), new("sonnet", "Sonnet"), new("haiku", "Haiku")];

    /// <summary>`agy models` prints "id\tLabel" per line (plus a "Fetching…" banner).</summary>
    public static IReadOnlyList<ModelInfo> Agy(string output) =>
        output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Contains('\t'))
            .Select(l => l.Split('\t', 2)).Select(p => new ModelInfo(p[0].Trim(), p[1].Trim())).ToList();

    /// <summary>A string property, or null when the entry isn't an object or the field is missing, null or not a string.</summary>
    private static string? Text(JsonElement entry, string name) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static IReadOnlyList<ModelInfo> OpenAi(string json) => Parse(json, root =>
        root.GetProperty("data").EnumerateArray().Select(m => Text(m, "id")).OfType<string>().Where(id => id.Length > 0)
            .Order(StringComparer.Ordinal).Select(id => new ModelInfo(id, id)).ToList());

    public static IReadOnlyList<ModelInfo> Gemini(string json) => Parse(json, root =>
        root.TryGetProperty("models", out var models)
            ? models.EnumerateArray()
                .Where(m => m.ValueKind == JsonValueKind.Object && m.TryGetProperty("supportedGenerationMethods", out var g)
                    && g.ValueKind == JsonValueKind.Array && g.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "generateContent"))
                .Select(m => Text(m, "name")?.Replace("models/", "", StringComparison.Ordinal) is { Length: > 0 } id
                    ? new ModelInfo(id, Text(m, "displayName") ?? id) : null)
                .OfType<ModelInfo>().ToList()
            : []);

    public static IReadOnlyList<ModelInfo> Anthropic(string json) => Parse(json, root =>
        root.GetProperty("data").EnumerateArray()
            .Select(m => Text(m, "id") is { Length: > 0 } id ? new ModelInfo(id, Text(m, "display_name") ?? id) : null)
            .OfType<ModelInfo>().ToList());

    private static IReadOnlyList<ModelInfo> Parse(string json, Func<JsonElement, IReadOnlyList<ModelInfo>> read)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return read(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ModelListException(Strings.Models_BadFormat);
        }
    }
}

/// <summary>Lists the models a connection offers, cached for 10 minutes per connection.</summary>
public sealed class ModelCatalog(HttpClient http, ISecretStore secrets, Func<BackendProfile, CancellationToken, Task<string>> runAgyModels, TimeProvider clock,
    Func<BackendProfile, CancellationToken, Task<IReadOnlyList<ModelInfo>>>? listCodexModels = null)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    // Concurrent callers (the picker and the Test button) share it across awaits.
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<ModelInfo> Models)> _cache = new();

    /// <summary>Most bytes read from a model-list answer (injectable for tests).</summary>
    public int MaxBodyBytes { get; init; } = Api.ApiCommon.MaxModelListBytes;

    public async Task<IReadOnlyList<ModelInfo>> GetAsync(BackendProfile p, bool refresh, CancellationToken ct)
    {
        var key = $"{p.Type}|{p.Id}|{p.Endpoint}|{p.CliPath}";
        if (!refresh && _cache.TryGetValue(key, out var hit) && clock.GetUtcNow() - hit.At < Ttl) return hit.Models;
        var models = await FetchAsync(p, ct);
        _cache[key] = (clock.GetUtcNow(), models);
        return models;
    }

    /// <summary>
    /// Settings › AI connections › Test: lists the models afresh (proves the endpoint and key) and checks the chosen
    /// model is among them. Throws <see cref="ModelListException"/> with the server's message on failure.
    /// </summary>
    public async Task<string> TestAsync(BackendProfile p, CancellationToken ct) =>
        Describe(await GetAsync(p, refresh: true, ct), p.Model, ConnectionTypes.Of(p.Type).ListsModels);

    /// <summary>"Connected: N models[, including X | , but X isn't one of them]."</summary>
    public static string Describe(IReadOnlyList<ModelInfo> models, string? chosen, bool listsModels = true)
    {
        var count = models.Count == 1 ? Strings.Models_One : L.F(Strings.Models_Many, models.Count);
        var model = chosen?.Trim();
        if (model is { Length: > 0 } && model.StartsWith("models/", StringComparison.Ordinal)) model = model[7..];
        if (!listsModels)
            return string.IsNullOrEmpty(model) ? Strings.Models_NoList : L.F(Strings.Models_Unlisted, model);
        if (string.IsNullOrEmpty(model) || models.Count == 0) return L.F(Strings.Models_Connected, count);
        return models.Any(m => string.Equals(m.Id, model, StringComparison.OrdinalIgnoreCase))
            ? L.F(Strings.Models_Including, count, model)
            : L.F(Strings.Models_NotIncluded, count, model);
    }

    private async Task<IReadOnlyList<ModelInfo>> FetchAsync(BackendProfile p, CancellationToken ct)
    {
        switch (p.Type)
        {
            case BackendType.Antigravity:
                return ModelListParsers.Agy(await runAgyModels(p, ct));
            case BackendType.ClaudeCode:
                return ModelListParsers.ClaudeCodeAliases;
            case BackendType.Codex:
                return listCodexModels is null ? Codex.CodexModels.Curated : await listCodexModels(p, ct);
            case BackendType.Gemini:
                return ModelListParsers.Gemini(await GetAsync(p, "models?pageSize=1000", (r, k) => r.Headers.Add("x-goog-api-key", k), ct));
            case BackendType.Anthropic:
                return ModelListParsers.Anthropic(await GetAsync(p, "models?limit=100", (r, k) =>
                {
                    r.Headers.Add("x-api-key", k);
                    r.Headers.Add("anthropic-version", "2023-06-01");
                }, ct));
            default: // OpenAiCompatible, Local
                return ModelListParsers.OpenAi(await GetAsync(p, "models", (r, k) =>
                    r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", k), ct));
        }
    }

    private async Task<string> GetAsync(BackendProfile p, string path, Action<HttpRequestMessage, string> addKey, CancellationToken ct)
    {
        var info = ConnectionTypes.Of(p.Type);
        var key = secrets.Get(SecretKeys.ApiKey(p.Id));
        if (string.IsNullOrEmpty(key) && !info.ApiKeyOptional)
            throw new ModelListException(L.F(Strings.Models_NeedKey, p.Name));
        var endpoint = (string.IsNullOrWhiteSpace(p.Endpoint) ? info.DefaultEndpoint : p.Endpoint)!.Trim().TrimEnd('/');
        // A hand-edited endpoint must end as a normal "couldn't list models", never as a UriFormatException.
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || !Uri.TryCreate($"{endpoint}/{path}", UriKind.Absolute, out var target))
            throw new ModelListException(L.F(Strings.Api_BadEndpoint, p.Name, endpoint));
        if (!string.IsNullOrEmpty(key) && uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new ModelListException($"Not sending the API key for {p.Name} over plain http to {uri.Host}; use an https:// endpoint.");
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        if (!string.IsNullOrEmpty(key)) addKey(request, key);
        // Headers first, so the size cap applies before the whole body is buffered; the time limit then covers the body too.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            // Never followed: the key headers would go along to wherever it points.
            if (Api.ApiCommon.RedirectTarget(response, target) is { } moved)
                throw new ModelListException(L.F(Strings.Api_Redirected, p.Name, moved));
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new ModelListException(L.F(Strings.Models_KeyRejected, p.Name));
            if (!response.IsSuccessStatusCode)
            {
                var detail = Api.ApiCommon.ErrorMessage((await Api.ApiCommon.ReadCappedAsync(response.Content, Api.ApiCommon.MaxErrorBodyBytes, limit.Token)).Text);
                throw new ModelListException(L.F(Strings.Models_HttpError, p.Name, $"{(int)response.StatusCode} {response.ReasonPhrase}") + (detail is { Length: > 0 } ? $" {detail}" : ""));
            }
            var (body, truncated) = await Api.ApiCommon.ReadCappedAsync(response.Content, MaxBodyBytes, limit.Token);
            return truncated ? throw new ModelListException(L.F(Strings.Api_TooLarge, p.Name)) : body;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            throw new ModelListException(L.F(Strings.Api_Unreachable, endpoint, ex.Message));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ModelListException(L.F(Strings.Api_Timeout, endpoint));
        }
    }
}
