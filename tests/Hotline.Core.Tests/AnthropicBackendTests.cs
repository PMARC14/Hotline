using System.Net;
using System.Text;
using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Api;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Anthropic Messages API via the official SDK, against a fake HTTP handler (no network, no key).</summary>
public sealed class AnthropicBackendTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Seen { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return respond(request);
        }
    }

    private static string Event(string name, object data) => $"event: {name}\ndata: {JsonSerializer.Serialize(data)}\n\n";

    private static HttpResponseMessage Stream(string stopReason = "end_turn", params string[] texts)
    {
        var sb = new StringBuilder();
        sb.Append(Event("message_start", new
        {
            type = "message_start",
            message = new { id = "msg_1", type = "message", role = "assistant", model = "claude-opus-5-5", content = Array.Empty<object>(), stop_reason = (string?)null, stop_sequence = (string?)null, usage = new { input_tokens = 5, output_tokens = 1 } },
        }));
        sb.Append(Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } }));
        foreach (var t in texts) sb.Append(Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = t } }));
        sb.Append(Event("content_block_stop", new { type = "content_block_stop", index = 0 }));
        sb.Append(Event("message_delta", new { type = "message_delta", delta = new { stop_reason = stopReason, stop_sequence = (string?)null }, usage = new { output_tokens = 3 } }));
        sb.Append(Event("message_stop", new { type = "message_stop" }));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream") };
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static ChatMessage U(string id, string text, params Attachment[] a) => new(id, ChatRole.User, text, a, Now);

    private (AnthropicBackend, Handler) New(BackendProfile profile, Func<HttpRequestMessage, HttpResponseMessage> respond, string? key = "sk-ant-test")
    {
        var handler = new Handler(respond);
        var secrets = new InMemorySecretStore();
        if (key is not null) secrets.Set(SecretKeys.ApiKey(profile.Id), key);
        return (new AnthropicBackend(profile, new HttpClient(handler), secrets, _ => "Be brief.", new FileLog(Path.Combine(_dir, "h.log"))), handler);
    }

    private static async Task<string> Collect(IAsyncEnumerable<ChatDelta> s)
    {
        var text = "";
        await foreach (var d in s) text = d.ResetBefore ? d.Text : text + d.Text;
        return text;
    }

    private static BackendProfile Claude(string? model = null, string? effort = null) =>
        new() { Id = "claude-api", Name = "Claude API", Type = BackendType.Anthropic, Model = model, Effort = effort };

    [Fact]
    public async Task Streams_text_with_default_model_system_prompt_image_and_fallback()
    {
        var (b, handler) = New(Claude(), _ => Stream(texts: ["Hel", "lo"]));
        var img = new Attachment("a", "x.png", AttachmentKind.Image, "image/png", [9, 9]);
        Assert.Equal("Hello", await Collect(b.StreamAsync([U("1", "look", img)], default)));

        var (request, body) = Assert.Single(handler.Seen);
        Assert.Equal("https://api.anthropic.com/v1/messages?beta=true", request.RequestUri!.ToString().Replace("/v1/messages?beta=true", "/v1/messages?beta=true"));
        Assert.Equal("sk-ant-test", request.Headers.GetValues("x-api-key").Single());
        Assert.Contains("server-side-fallback-2026-07-01", string.Join(",", request.Headers.GetValues("anthropic-beta")));
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal(AnthropicBackend.DefaultModel, json.GetProperty("model").GetString());
        Assert.True(json.GetProperty("stream").GetBoolean());
        Assert.Equal("default", json.GetProperty("fallbacks").GetString());
        Assert.Contains("Be brief.", json.GetProperty("system").ToString());
        Assert.False(json.TryGetProperty("thinking", out _)); // adaptive default
        var content = json.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("image", content[1].GetProperty("type").GetString());
        Assert.Equal("image/png", content[1].GetProperty("source").GetProperty("media_type").GetString());
    }

    [Fact]
    public async Task Effort_goes_in_output_config_and_fallback_only_for_supported_models()
    {
        var (b, handler) = New(Claude("claude-haiku-4-5", "low"), _ => Stream(texts: ["ok"]));
        await Collect(b.StreamAsync([U("1", "hi")], default));
        var json = JsonDocument.Parse(handler.Seen[0].Body).RootElement;
        Assert.Equal("low", json.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.False(json.TryGetProperty("fallbacks", out _));
    }

    [Fact]
    public async Task Refusal_is_reported()
    {
        var (b, _) = New(Claude(), _ => Stream("refusal"));
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([U("1", "hi")], default)));
        Assert.Contains("declined", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, BackendErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, BackendErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, BackendErrorKind.ServerDown)]
    [InlineData(HttpStatusCode.BadRequest, BackendErrorKind.Failed)]
    public async Task Api_errors_map_to_kinds(HttpStatusCode code, BackendErrorKind kind)
    {
        var (b, _) = New(Claude(), _ => new HttpResponseMessage(code)
        {
            Content = new StringContent("""{"type":"error","error":{"type":"x","message":"something specific"}}""", Encoding.UTF8, "application/json"),
        });
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([U("1", "hi")], default)));
        Assert.Equal(kind, ex.Kind);
    }

    [Fact]
    public async Task Missing_key_is_not_configured_and_nothing_is_sent()
    {
        var (b, handler) = New(Claude(), _ => Stream(texts: ["x"]), key: null);
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.NotConfigured, ex.Kind);
        Assert.Empty(handler.Seen);
    }

    private static HttpResponseMessage ToolUseStream()
    {
        var sb = new StringBuilder();
        sb.Append(Event("message_start", new
        {
            type = "message_start",
            message = new { id = "msg_t", type = "message", role = "assistant", model = "claude-opus-5-5", content = Array.Empty<object>(), stop_reason = (string?)null, stop_sequence = (string?)null, usage = new { input_tokens = 5, output_tokens = 1 } },
        }));
        sb.Append(Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "thinking", thinking = "", signature = "" } }));
        sb.Append(Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "signature_delta", signature = "SIG123" } }));
        sb.Append(Event("content_block_stop", new { type = "content_block_stop", index = 0 }));
        sb.Append(Event("content_block_start", new { type = "content_block_start", index = 1, content_block = new { type = "tool_use", id = "toolu_1", name = "files__read_file", input = new { } } }));
        sb.Append(Event("content_block_delta", new { type = "content_block_delta", index = 1, delta = new { type = "input_json_delta", partial_json = "{\"path\":" } }));
        sb.Append(Event("content_block_delta", new { type = "content_block_delta", index = 1, delta = new { type = "input_json_delta", partial_json = "\"a.txt\"}" } }));
        sb.Append(Event("content_block_stop", new { type = "content_block_stop", index = 1 }));
        sb.Append(Event("message_delta", new { type = "message_delta", delta = new { stop_reason = "tool_use", stop_sequence = (string?)null }, usage = new { output_tokens = 9 } }));
        sb.Append(Event("message_stop", new { type = "message_stop" }));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream") };
    }

    [Fact]
    public async Task Tool_use_runs_the_tool_and_sends_back_thinking_tool_use_and_result()
    {
        var host = new ToolLoopTests.FakeToolHost();
        var calls = 0;
        var handler = new Handler(_ => ++calls == 1 ? ToolUseStream() : Stream(texts: ["It says hi."]));
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretKeys.ApiKey("claude-api"), "sk-ant-test");
        var profile = Claude();
        profile.Tools = ToolMode.Inherit;
        var b = new AnthropicBackend(profile, new HttpClient(handler), secrets, _ => "Be brief.", new FileLog(Path.Combine(_dir, "h.log")), host);

        var text = await Collect(b.StreamAsync([U("1", "read a.txt")], default));

        Assert.Equal(("files__read_file", """{"path":"a.txt"}"""), Assert.Single(host.Calls));
        Assert.EndsWith("It says hi.", text);
        var first = JsonDocument.Parse(handler.Seen[0].Body).RootElement;
        Assert.Equal("files__read_file", first.GetProperty("tools")[0].GetProperty("name").GetString());
        var messages = JsonDocument.Parse(handler.Seen[1].Body).RootElement.GetProperty("messages").EnumerateArray().ToList();
        var assistant = messages[^2].GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(("thinking", "SIG123"), (assistant[0].GetProperty("type").GetString(), assistant[0].GetProperty("signature").GetString()));
        Assert.Equal(("tool_use", "toolu_1"), (assistant[1].GetProperty("type").GetString(), assistant[1].GetProperty("id").GetString()));
        Assert.Equal("a.txt", assistant[1].GetProperty("input").GetProperty("path").GetString());
        var result = messages[^1].GetProperty("content")[0];
        Assert.Equal(("tool_result", "toolu_1"), (result.GetProperty("type").GetString(), result.GetProperty("tool_use_id").GetString()));
    }
}
