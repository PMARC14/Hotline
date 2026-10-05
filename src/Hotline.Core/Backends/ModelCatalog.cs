using System.Text.Json;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

public sealed record ModelInfo(string Id, string Label);

public sealed class ModelListException(string message) : Exception(message);

public static class ModelListParsers
{
    public static IReadOnlyList<ModelInfo> ClaudeCodeAliases { get; } =
        [new("fable", "Fable"), new("opus", "Opus"), new("sonnet", "Sonnet"), new("haiku", "Haiku")];

    /// <summary>`agy models` prints "id\tLabel" per line (plus a "Fetching…" banner).</summary>
    public static IReadOnlyList<ModelInfo> Agy(string output) =>
        output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Contains('\t'))
            .Select(l => l.Split('\t', 2)).Select(p => new ModelInfo(p[0].Trim(), p[1].Trim())).ToList();

    public static IReadOnlyList<ModelInfo> OpenAi(string json) => Parse(json, root =>
        root.GetProperty("data").EnumerateArray().Select(m => m.GetProperty("id").GetString()!)
            .Order(StringComparer.Ordinal).Select(id => new ModelInfo(id, id)).ToList());

    public static IReadOnlyList<ModelInfo> Gemini(string json) => Parse(json, root =>
        root.TryGetProperty("models", out var models)
            ? models.EnumerateArray()
                .Where(m => m.TryGetProperty("supportedGenerationMethods", out var g) && g.EnumerateArray().Any(x => x.GetString() == "generateContent"))
                .Select(m =>
                {
                    var id = m.GetProperty("name").GetString()!.Replace("models/", "", StringComparison.Ordinal);
                    return new ModelInfo(id, m.TryGetProperty("displayName", out var d) ? d.GetString() ?? id : id);
                }).ToList()
            : []);

    public static IReadOnlyList<ModelInfo> Anthropic(string json) => Parse(json, root =>
        root.GetProperty("data").EnumerateArray().Select(m =>
        {
            var id = m.GetProperty("id").GetString()!;
            return new ModelInfo(id, m.TryGetProperty("display_name", out var d) ? d.GetString() ?? id : id);
        }).ToList());

    private static IReadOnlyList<ModelInfo> Parse(string json, Func<JsonElement, IReadOnlyList<ModelInfo>> read)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return read(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ModelListException("The server's model list wasn't in the expected format.");
        }
    }
}

/// <summary>Lists the models a connection offers, cached for 10 minutes per connection.</summary>
public sealed class ModelCatalog(HttpClient http, ISecretStore secrets, Func<BackendProfile, CancellationToken, Task<string>> runAgyModels, TimeProvider clock)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, (DateTimeOffset At, IReadOnlyList<ModelInfo> Models)> _cache = [];

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
    public async Task<string> TestAsync(BackendProfile p, CancellationToken ct) => Describe(await GetAsync(p, refresh: true, ct), p.Model);

    /// <summary>"Connected: N models[, including X | , but X isn't one of them]."</summary>
    public static string Describe(IReadOnlyList<ModelInfo> models, string? chosen)
    {
        var count = models.Count == 1 ? "1 model" : $"{models.Count} models";
        var model = chosen?.Trim();
        if (model is { Length: > 0 } && model.StartsWith("models/", StringComparison.Ordinal)) model = model[7..];
        if (string.IsNullOrEmpty(model) || models.Count == 0) return $"Connected: {count}.";
        return models.Any(m => string.Equals(m.Id, model, StringComparison.OrdinalIgnoreCase))
            ? $"Connected: {count}, including {model}."
            : $"Connected: {count}, but {model} isn't one of them.";
    }

    private async Task<IReadOnlyList<ModelInfo>> FetchAsync(BackendProfile p, CancellationToken ct)
    {
        switch (p.Type)
        {
            case BackendType.Antigravity:
                return ModelListParsers.Agy(await runAgyModels(p, ct));
            case BackendType.ClaudeCode:
                return ModelListParsers.ClaudeCodeAliases;
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
            throw new ModelListException($"Add an API key for {p.Name} first.");
        var endpoint = (string.IsNullOrWhiteSpace(p.Endpoint) ? info.DefaultEndpoint : p.Endpoint)!.TrimEnd('/');
        if (!string.IsNullOrEmpty(key) && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new ModelListException($"Not sending the API key for {p.Name} over plain http to {uri.Host}; use an https:// endpoint.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/{path}");
        if (!string.IsNullOrEmpty(key)) addKey(request, key);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new ModelListException($"{p.Name} rejected the API key.");
            if (!response.IsSuccessStatusCode)
            {
                var detail = Api.ApiCommon.ErrorMessage(await response.Content.ReadAsStringAsync(ct));
                throw new ModelListException($"{p.Name} answered {(int)response.StatusCode} {response.ReasonPhrase}." + (detail is { Length: > 0 } ? $" {detail}" : ""));
            }
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ModelListException($"Can't reach {endpoint}: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ModelListException($"{endpoint} didn't answer in time.");
        }
    }
}
