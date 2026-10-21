using System.Net;
using System.Text;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Api;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Security S2a: redirects never carry a key, response sizes are capped, bad endpoints and odd JSON don't throw.</summary>
public sealed class HttpHardeningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Redirect(string location, HttpStatusCode code = HttpStatusCode.TemporaryRedirect)
    {
        var r = new HttpResponseMessage(code);
        r.Headers.TryAddWithoutValidation("Location", location);
        return r;
    }

    private static InMemorySecretStore KeyFor(string id)
    {
        var s = new InMemorySecretStore();
        s.Set(SecretKeys.ApiKey(id), "sk-secret");
        return s;
    }

    private async Task<BackendException> OpenAiFailure(Handler handler)
    {
        var p = new BackendProfile { Id = "oa", Name = "OpenAI", Type = BackendType.OpenAiCompatible, Model = "m", Endpoint = "https://api.example.com/v1" };
        var backend = new OpenAiBackend(p, new HttpClient(handler), KeyFor("oa"), _ => "", new FileLog(Path.Combine(_dir, "h.log")));
        return await Assert.ThrowsAsync<BackendException>(async () =>
        {
            await foreach (var _ in backend.StreamAsync([new ChatMessage("1", ChatRole.User, "hi", [], DateTimeOffset.UnixEpoch)], default)) { }
        });
    }

    [Fact]
    public async Task Chat_redirect_is_not_followed_and_names_only_the_new_host()
    {
        var handler = new Handler(_ => Redirect("http://attacker.example:8080/steal?token=abc"));
        var ex = await OpenAiFailure(handler);
        Assert.Single(handler.Urls); // the key header went to the configured address only
        Assert.Contains("attacker.example:8080", ex.Message);
        Assert.DoesNotContain("token", ex.Message);
        Assert.DoesNotContain("/steal", ex.Message);
        Assert.DoesNotContain("sk-secret", ex.Message);
    }

    [Fact]
    public async Task Model_list_redirect_is_not_followed()
    {
        var handler = new Handler(_ => Redirect("/elsewhere?k=1", HttpStatusCode.Found));
        var catalog = new ModelCatalog(new HttpClient(handler), KeyFor("oa"), (_, _) => Task.FromResult(""), TimeProvider.System);
        var p = new BackendProfile { Id = "oa", Name = "OpenAI", Type = BackendType.OpenAiCompatible, Endpoint = "https://api.example.com/v1" };
        var ex = await Assert.ThrowsAsync<ModelListException>(() => catalog.GetAsync(p, false, default));
        Assert.Single(handler.Urls);
        Assert.Contains("api.example.com", ex.Message);
        Assert.DoesNotContain("k=1", ex.Message);
    }

    [Fact]
    public async Task Sse_line_over_the_limit_fails_clearly()
    {
        var bytes = Encoding.UTF8.GetBytes("data: " + new string('x', 5000)); // no newline at all
        var ex = await Assert.ThrowsAsync<BackendException>(async () =>
        {
            await foreach (var _ in ApiCommon.SseData(new MemoryStream(bytes), default, maxChars: 1000)) { }
        });
        Assert.Equal(BackendErrorKind.Failed, ex.Kind);
    }

    [Fact]
    public async Task Sse_event_made_of_many_small_data_lines_is_capped_too()
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("data: " + new string('y', 90) + "\n", 50)));
        await Assert.ThrowsAsync<BackendException>(async () =>
        {
            await foreach (var _ in ApiCommon.SseData(new MemoryStream(bytes), default, maxChars: 1000)) { }
        });
    }

    [Fact]
    public async Task Sse_within_the_limit_still_reads_all_line_endings()
    {
        var bytes = Encoding.UTF8.GetBytes("data: a\r\n\r\ndata: b\r\rdata: c\n\n");
        var items = new List<string>();
        await foreach (var d in ApiCommon.SseData(new MemoryStream(bytes), default, maxChars: 1000)) items.Add(d);
        Assert.Equal(["a", "b", "c"], items);
    }

    [Fact]
    public async Task Sse_many_events_each_under_the_limit_are_fine()
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("data: " + new string('z', 400) + "\n\n", 100)));
        var count = 0;
        await foreach (var _ in ApiCommon.SseData(new MemoryStream(bytes), default, maxChars: 1000)) count++;
        Assert.Equal(100, count);
    }

    [Fact]
    public async Task Capped_read_stops_at_the_limit_and_says_so()
    {
        using var content = new StringContent(new string('e', 5000));
        var (text, truncated) = await ApiCommon.ReadCappedAsync(content, 1000, default);
        Assert.True(truncated);
        Assert.Equal(1000, text.Length);
        using var small = new StringContent("ok");
        Assert.Equal(("ok", false), await ApiCommon.ReadCappedAsync(small, 1000, default));
    }

    [Fact]
    public async Task Oversized_error_body_does_not_break_the_error_message()
    {
        var huge = "{\"error\":{\"message\":\"" + new string('a', 600_000) + "\"}}";
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(huge) });
        var ex = await OpenAiFailure(handler);
        Assert.True(ex.Message.Length < 300_000);
    }

    [Fact]
    public async Task Oversized_model_list_is_refused()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string(' ', 500)) });
        var catalog = new ModelCatalog(new HttpClient(handler), KeyFor("oa"), (_, _) => Task.FromResult(""), TimeProvider.System) { MaxBodyBytes = 100 };
        var p = new BackendProfile { Id = "oa", Name = "OpenAI", Type = BackendType.OpenAiCompatible, Endpoint = "https://api.example.com/v1" };
        await Assert.ThrowsAsync<ModelListException>(() => catalog.GetAsync(p, false, default));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/v1")]
    [InlineData("http://")]
    public async Task Model_list_with_a_bad_endpoint_is_a_normal_failure(string endpoint)
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var catalog = new ModelCatalog(new HttpClient(handler), new InMemorySecretStore(), (_, _) => Task.FromResult(""), TimeProvider.System);
        var p = new BackendProfile { Id = "oa", Name = "OpenAI", Type = BackendType.Local, Endpoint = endpoint };
        await Assert.ThrowsAsync<ModelListException>(() => catalog.GetAsync(p, false, default));
        Assert.Empty(handler.Urls);
    }

    [Fact]
    public void Safe_transport_checks_the_endpoint_even_without_a_key()
        => Assert.Throws<BackendException>(() => ApiCommon.RequireSafeTransport(new BackendProfile { Name = "X" }, "not a url", null));

    [Fact]
    public void Parsers_skip_entries_with_null_or_wrong_typed_fields()
    {
        Assert.Equal(["ok"], ModelListParsers.OpenAi("""{"data":[{"id":null},{"id":5},"x",null,{"id":"ok"}]}""").Select(m => m.Id));
        var gemini = ModelListParsers.Gemini("""
            {"models":[
              {"name":null,"supportedGenerationMethods":["generateContent"]},
              {"name":7,"supportedGenerationMethods":["generateContent"]},
              {"name":"models/a","displayName":3,"supportedGenerationMethods":["generateContent"]},
              {"name":"models/b","supportedGenerationMethods":null},
              {"name":"models/c","supportedGenerationMethods":"generateContent"},
              null]}
            """);
        Assert.Equal([new ModelInfo("a", "a")], gemini);
        Assert.Equal([new ModelInfo("m", "m")], ModelListParsers.Anthropic("""{"data":[{"id":null},{"id":"m","display_name":null},{"display_name":"x"}]}"""));
    }

    [Fact]
    public async Task Concurrent_lookups_share_the_cache_without_errors()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":[{"id":"a"}]}""") });
        var catalog = new ModelCatalog(new HttpClient(handler), new InMemorySecretStore(), (_, _) => Task.FromResult(""), TimeProvider.System);
        var tasks = Enumerable.Range(0, 200).Select(i => Task.Run(() => catalog.GetAsync(
            new BackendProfile { Id = $"c{i % 20}", Name = "L", Type = BackendType.Local, Endpoint = "http://localhost:1/v1" }, i % 3 == 0, default)));
        foreach (var models in await Task.WhenAll(tasks)) Assert.Equal("a", Assert.Single(models).Id);
    }
}
