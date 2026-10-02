using System.Net;
using System.Text;
using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Api;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class OpenAiBackendTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private sealed class Handler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Seen { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Seen.Add((request, body));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Sse(params string[] data) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Concat(data.Select(d => $"data: {d}\n\n")), Encoding.UTF8, "text/event-stream"),
    };

    private static string Chunk(string text) => JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = text } } } });

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static ChatMessage U(string id, string text, params Attachment[] a) => new(id, ChatRole.User, text, a, Now);
    private static ChatMessage A(string id, string text) => new(id, ChatRole.Assistant, text, [], Now);

    private (OpenAiBackend, Handler, InMemorySecretStore) New(BackendProfile profile, Func<HttpRequestMessage, string, HttpResponseMessage> respond)
    {
        var handler = new Handler(respond);
        var secrets = new InMemorySecretStore();
        return (new OpenAiBackend(profile, new HttpClient(handler), secrets, _ => "Be brief.", new FileLog(Path.Combine(_dir, "h.log"))), handler, secrets);
    }

    private static async Task<string> Collect(IAsyncEnumerable<ChatDelta> s)
    {
        var text = "";
        await foreach (var d in s) text = d.ResetBefore ? d.Text : text + d.Text;
        return text;
    }

    [Fact]
    public async Task Streams_chat_completions_with_system_prompt_history_and_key()
    {
        var (b, handler, secrets) = New(new BackendProfile { Id = "oa", Name = "OpenRouter", Type = BackendType.OpenAiCompatible, Endpoint = "https://openrouter.ai/api/v1/", Model = "x/model" },
            (_, _) => Sse(Chunk("Hel"), Chunk("lo"), """{"choices":[{"delta":{},"finish_reason":"stop"}]}""", "[DONE]"));
        secrets.Set(SecretKeys.ApiKey("oa"), "sk-test");
        var text = await Collect(b.StreamAsync([U("1", "hi"), A("2", "hey"), U("3", "again")], default));
        Assert.Equal("Hello", text);
        var (request, body) = Assert.Single(handler.Seen);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", request.Headers.Authorization!.ToString());
        var json = JsonDocument.Parse(body).RootElement;
        Assert.True(json.GetProperty("stream").GetBoolean());
        Assert.Equal("x/model", json.GetProperty("model").GetString());
        var messages = json.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["system", "user", "assistant", "user"], messages.Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("Be brief.", messages[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Images_go_as_data_urls_in_content_parts()
    {
        var (b, handler, _) = New(new BackendProfile { Id = "l", Type = BackendType.Local, Model = "qwen" }, (_, _) => Sse(Chunk("ok"), "[DONE]"));
        await Collect(b.StreamAsync([U("1", "what is this", new Attachment("a", "x.png", AttachmentKind.Image, "image/png", [1, 2]))], default));
        var content = JsonDocument.Parse(handler.Seen[0].Body).RootElement.GetProperty("messages")[1].GetProperty("content");
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(new byte[] { 1, 2 }), content[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public async Task Local_server_needs_no_key_or_model()
    {
        var (b, handler, _) = New(new BackendProfile { Id = "l", Type = BackendType.Local }, (_, _) => Sse(Chunk("ok"), "[DONE]"));
        Assert.Equal("ok", await Collect(b.StreamAsync([U("1", "hi")], default)));
        Assert.Equal("http://127.0.0.1:8080/v1/chat/completions", handler.Seen[0].Request.RequestUri!.ToString());
        Assert.Null(handler.Seen[0].Request.Headers.Authorization);
    }

    [Fact]
    public async Task Hosted_api_without_key_or_model_is_not_configured()
    {
        var (b, handler, _) = New(new BackendProfile { Id = "oa", Name = "OpenAI", Type = BackendType.OpenAiCompatible, Model = "gpt-x" }, (_, _) => Sse("[DONE]"));
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.NotConfigured, ex.Kind);
        Assert.Contains("API key", ex.Message);
        var (b2, _, s2) = New(new BackendProfile { Id = "oa", Name = "OpenAI", Type = BackendType.OpenAiCompatible }, (_, _) => Sse("[DONE]"));
        s2.Set(SecretKeys.ApiKey("oa"), "k");
        Assert.Contains("model", (await Assert.ThrowsAsync<BackendException>(() => Collect(b2.StreamAsync([U("1", "hi")], default)))).Message);
        Assert.Empty(handler.Seen);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, BackendErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, BackendErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, BackendErrorKind.ServerDown)]
    [InlineData(HttpStatusCode.BadRequest, BackendErrorKind.Failed)]
    public async Task Http_errors_map_to_kinds_with_the_servers_message(HttpStatusCode code, BackendErrorKind kind)
    {
        var (b, _, s) = New(new BackendProfile { Id = "oa", Name = "X", Type = BackendType.OpenAiCompatible, Model = "m" },
            (_, _) => new HttpResponseMessage(code) { Content = new StringContent("""{"error":{"message":"model not found: m"}}""") });
        s.Set(SecretKeys.ApiKey("oa"), "k");
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([U("1", "hi")], default)));
        Assert.Equal(kind, ex.Kind);
        if (kind == BackendErrorKind.Failed) Assert.Contains("model not found", ex.Message);
    }

    [Fact]
    public async Task Unreachable_local_server_is_server_down()
    {
        var (b, _, _) = New(new BackendProfile { Id = "l", Name = "llama", Type = BackendType.Local }, (_, _) => throw new HttpRequestException("refused"));
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.ServerDown, ex.Kind);
        Assert.Contains("127.0.0.1:8080", ex.Message);
    }

    [Fact]
    public async Task Error_in_the_stream_is_reported()
    {
        var (b, _, _) = New(new BackendProfile { Id = "l", Type = BackendType.Local }, (_, _) => Sse(Chunk("par"), """{"error":{"message":"context length exceeded"}}"""));
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([U("1", "hi")], default)));
        Assert.Contains("context length", ex.Message);
    }

    [Fact]
    public async Task Reasoning_effort_is_sent_when_set()
    {
        var (b, handler, s) = New(new BackendProfile { Id = "oa", Type = BackendType.OpenAiCompatible, Model = "m", Effort = "high" }, (_, _) => Sse(Chunk("ok"), "[DONE]"));
        s.Set(SecretKeys.ApiKey("oa"), "k");
        await Collect(b.StreamAsync([U("1", "hi")], default));
        Assert.Equal("high", JsonDocument.Parse(handler.Seen[0].Body).RootElement.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task Key_is_never_sent_over_plain_http_to_another_host()
    {
        var (b, handler, s) = New(new BackendProfile { Id = "oa", Name = "X", Type = BackendType.OpenAiCompatible, Model = "m", Endpoint = "http://example.com/v1" }, (_, _) => Sse("[DONE]"));
        s.Set(SecretKeys.ApiKey("oa"), "k");
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([U("1", "hi")], default)));
        Assert.Contains("https", ex.Message);
        Assert.Empty(handler.Seen);
    }
}
