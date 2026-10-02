using System.Net;
using Hotline.Core.Backends;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class ModelCatalogTests
{
    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body) };

    private static (ModelCatalog, FakeHttp, InMemorySecretStore, ManualTimeProvider) New(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var http = new FakeHttp(respond);
        var secrets = new InMemorySecretStore();
        var clock = new ManualTimeProvider();
        return (new ModelCatalog(new HttpClient(http), secrets, (_, _) => Task.FromResult("m1\tModel One\n"), clock), http, secrets, clock);
    }

    [Fact]
    public async Task OpenAi_compatible_sends_bearer_key_to_endpoint_models()
    {
        var (catalog, http, secrets, _) = New(_ => Json("""{"data":[{"id":"gpt-x"}]}"""));
        var p = new BackendProfile { Id = "oa", Type = BackendType.OpenAiCompatible, Endpoint = "https://api.example.com/v1/" };
        secrets.Set(SecretKeys.ApiKey("oa"), "sk-test");
        var models = await catalog.GetAsync(p, refresh: false, default);
        Assert.Equal("gpt-x", Assert.Single(models).Id);
        Assert.Equal("https://api.example.com/v1/models", http.Requests[0].RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", http.Requests[0].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Gemini_uses_goog_header_and_default_endpoint()
    {
        var (catalog, http, secrets, _) = New(_ => Json("""{"models":[]}"""));
        secrets.Set(SecretKeys.ApiKey("g"), "AIza");
        await catalog.GetAsync(new BackendProfile { Id = "g", Type = BackendType.Gemini }, false, default);
        Assert.StartsWith("https://generativelanguage.googleapis.com/v1beta/models", http.Requests[0].RequestUri!.ToString());
        Assert.Equal("AIza", http.Requests[0].Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task Anthropic_sends_key_and_version()
    {
        var (catalog, http, secrets, _) = New(_ => Json("""{"data":[]}"""));
        secrets.Set(SecretKeys.ApiKey("a"), "sk-ant");
        await catalog.GetAsync(new BackendProfile { Id = "a", Type = BackendType.Anthropic }, false, default);
        Assert.Equal("sk-ant", http.Requests[0].Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", http.Requests[0].Headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public async Task Missing_required_key_is_reported_without_calling()
    {
        var (catalog, http, _, _) = New(_ => Json("{}"));
        var ex = await Assert.ThrowsAsync<ModelListException>(() => catalog.GetAsync(new BackendProfile { Id = "g", Type = BackendType.Gemini }, false, default));
        Assert.Contains("API key", ex.Message);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Local_endpoint_works_without_key()
    {
        var (catalog, http, _, _) = New(_ => Json("""{"data":[{"id":"qwen"}]}"""));
        var models = await catalog.GetAsync(new BackendProfile { Id = "l", Type = BackendType.Local }, false, default);
        Assert.Equal("qwen", Assert.Single(models).Id);
        Assert.Null(http.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task Unauthorized_is_reported()
    {
        var (catalog, _, secrets, _) = New(_ => Json("""{"error":"bad key"}""", HttpStatusCode.Unauthorized));
        secrets.Set(SecretKeys.ApiKey("oa"), "bad");
        var ex = await Assert.ThrowsAsync<ModelListException>(() =>
            catalog.GetAsync(new BackendProfile { Id = "oa", Type = BackendType.OpenAiCompatible }, false, default));
        Assert.Contains("rejected", ex.Message);
    }

    [Fact]
    public async Task Unreachable_server_is_reported()
    {
        var (catalog, _, _, _) = New(_ => throw new HttpRequestException("connection refused"));
        var ex = await Assert.ThrowsAsync<ModelListException>(() =>
            catalog.GetAsync(new BackendProfile { Id = "l", Type = BackendType.Local }, false, default));
        Assert.Contains("reach", ex.Message);
    }

    [Fact]
    public async Task Results_are_cached_until_refresh_or_ttl()
    {
        var (catalog, http, _, clock) = New(_ => Json("""{"data":[{"id":"x"}]}"""));
        var p = new BackendProfile { Id = "l", Type = BackendType.Local };
        await catalog.GetAsync(p, false, default);
        await catalog.GetAsync(p, false, default);
        Assert.Single(http.Requests);
        await catalog.GetAsync(p, refresh: true, default);
        Assert.Equal(2, http.Requests.Count);
        clock.Advance(TimeSpan.FromMinutes(11));
        await catalog.GetAsync(p, false, default);
        Assert.Equal(3, http.Requests.Count);
    }

    [Fact]
    public async Task Agy_uses_cli_runner_and_claude_code_uses_aliases()
    {
        var (catalog, _, _, _) = New(_ => Json("{}"));
        Assert.Equal("m1", Assert.Single(await catalog.GetAsync(new BackendProfile { Id = "agy", Type = BackendType.Antigravity }, false, default)).Id);
        Assert.Contains(await catalog.GetAsync(new BackendProfile { Id = "cc", Type = BackendType.ClaudeCode }, false, default), m => m.Id == "sonnet");
    }

    [Fact]
    public async Task Key_is_never_sent_over_plain_http_to_another_host()
    {
        var (catalog, http, secrets, _) = New(_ => Json("""{"data":[]}"""));
        secrets.Set(SecretKeys.ApiKey("oa"), "sk");
        var ex = await Assert.ThrowsAsync<ModelListException>(() => catalog.GetAsync(
            new BackendProfile { Id = "oa", Name = "X", Type = BackendType.OpenAiCompatible, Endpoint = "http://example.com/v1" }, false, default));
        Assert.Contains("https", ex.Message);
        Assert.Empty(http.Requests);
        secrets.Set(SecretKeys.ApiKey("l"), "sk");
        await catalog.GetAsync(new BackendProfile { Id = "l", Type = BackendType.Local, Endpoint = "http://127.0.0.1:8080/v1" }, false, default); // loopback ok
    }
}
