using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Api;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Plan 6 task 2: effort pickers for Gemini/OpenAI, 429/503/529 retries, status notes.</summary>
public sealed class ApiPolishTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private sealed class Handler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return respond(Bodies.Count);
        }
    }

    private static HttpResponseMessage Sse(params string[] data) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Concat(data.Select(d => $"data: {d}\n\n")), Encoding.UTF8, "text/event-stream"),
    };

    private static HttpResponseMessage Status(int code, string body = "{\"error\":{\"message\":\"slow down\"}}", TimeSpan? retryAfter = null)
    {
        var r = new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is { } wait) r.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
        return r;
    }

    private static string OpenAiChunk(string text) => JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = text } } } });

    private static string GeminiPart(string text) => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { role = "model", parts = new[] { new { text } } } } },
    });

    private OpenAiBackend OpenAi(Handler handler, string? effort = null)
    {
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretKeys.ApiKey("oa"), "sk-test");
        var p = new BackendProfile { Id = "oa", Name = "OpenAI", Type = BackendType.OpenAiCompatible, Model = "gpt-x", Effort = effort };
        return new OpenAiBackend(p, new HttpClient(handler), secrets, _ => "", new FileLog(Path.Combine(_dir, "h.log")));
    }

    private GeminiBackend Gemini(Handler handler, string model, string? effort)
    {
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretKeys.ApiKey("g"), "AIza-test");
        var p = new BackendProfile { Id = "g", Name = "Gemini API", Type = BackendType.Gemini, Model = model, Effort = effort };
        return new GeminiBackend(p, new HttpClient(handler), secrets, _ => "", new FileLog(Path.Combine(_dir, "h.log")));
    }

    private static async Task<(string Text, List<string> Status)> Collect(IAsyncEnumerable<ChatDelta> s)
    {
        var text = "";
        var status = new List<string>();
        await foreach (var d in s)
        {
            if (d.Status is { } note) { status.Add(note); continue; }
            text = d.ResetBefore ? d.Text : text + d.Text;
        }
        return (text, status);
    }

    private static ChatMessage User(string text) => new("1", ChatRole.User, text, [], Now);

    // ---- effort levels ------------------------------------------------------------------------

    [Fact]
    public void Gemini_and_openai_offer_low_medium_high_and_local_offers_none()
    {
        Assert.Equal(["low", "medium", "high"], ConnectionTypes.Of(BackendType.Gemini).EffortLevels);
        Assert.Equal(["low", "medium", "high"], ConnectionTypes.Of(BackendType.OpenAiCompatible).EffortLevels);
        Assert.Empty(ConnectionTypes.Of(BackendType.Local).EffortLevels);
    }

    [Fact]
    public async Task Gemini_3_sends_thinking_level()
    {
        var handler = new Handler(_ => Sse(GeminiPart("ok")));
        await Collect(Gemini(handler, "gemini-3.8-flash", "High").StreamAsync([User("hi")], default));
        var config = JsonDocument.Parse(handler.Bodies[0]).RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig");
        Assert.Equal("high", config.GetProperty("thinkingLevel").GetString());
        Assert.False(config.TryGetProperty("thinkingBudget", out _));
    }

    [Fact]
    public async Task Gemini_2_5_gets_a_thinking_budget_instead()
    {
        var handler = new Handler(_ => Sse(GeminiPart("ok")));
        await Collect(Gemini(handler, "models/gemini-2.5-flash", "low").StreamAsync([User("hi")], default));
        var config = JsonDocument.Parse(handler.Bodies[0]).RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig");
        Assert.Equal(1024, config.GetProperty("thinkingBudget").GetInt32());
        Assert.False(config.TryGetProperty("thinkingLevel", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("max")]
    public async Task Gemini_without_a_known_effort_sends_no_thinking_config(string? effort)
    {
        var handler = new Handler(_ => Sse(GeminiPart("ok")));
        await Collect(Gemini(handler, "gemini-3.8-flash", effort).StreamAsync([User("hi")], default));
        Assert.False(JsonDocument.Parse(handler.Bodies[0]).RootElement.TryGetProperty("generationConfig", out _));
    }

    // ---- retry policy -------------------------------------------------------------------------

    [Fact]
    public void Retry_after_header_is_honoured()
    {
        Assert.Equal(TimeSpan.FromSeconds(7), ApiCommon.RetryDelay(Status(429, retryAfter: TimeSpan.FromSeconds(7)), "", attempt: 1, Now));
        var dated = Status(503);
        dated.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(5), ApiCommon.RetryDelay(dated, "", attempt: 1, Now));
    }

    [Fact]
    public void Gemini_retry_delay_in_the_body_is_honoured()
    {
        const string body = """{"error":{"code":429,"status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"12.5s"}]}}""";
        Assert.Equal(TimeSpan.FromSeconds(12.5), ApiCommon.RetryDelay(Status(429), body, attempt: 1, Now));
    }

    [Fact]
    public void Without_a_hint_waits_back_off_2_then_4_seconds_and_stop_after_three_tries()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), ApiCommon.RetryDelay(Status(529), "", attempt: 1, Now));
        Assert.Equal(TimeSpan.FromSeconds(4), ApiCommon.RetryDelay(Status(529), "", attempt: 2, Now));
        Assert.Null(ApiCommon.RetryDelay(Status(529), "", attempt: 3, Now));
    }

    [Theory]
    [InlineData(400, "")]
    [InlineData(401, "")]
    [InlineData(500, "")]
    [InlineData(429, """{"error":{"code":"insufficient_quota","message":"You exceeded your current quota"}}""")]
    public void Other_failures_and_exhausted_quota_are_not_retried(int code, string body) =>
        Assert.Null(ApiCommon.RetryDelay(Status(code), body, attempt: 1, Now));

    [Fact]
    public void A_wait_longer_than_a_minute_is_not_retried() =>
        Assert.Null(ApiCommon.RetryDelay(Status(429, retryAfter: TimeSpan.FromSeconds(61)), "", attempt: 1, Now));

    // ---- retries in the backends --------------------------------------------------------------

    [Fact]
    public async Task OpenAi_retries_a_429_and_shows_a_status_note()
    {
        var handler = new Handler(n => n == 1 ? Status(429, retryAfter: TimeSpan.Zero) : Sse(OpenAiChunk("Hello"), "[DONE]"));
        var (text, status) = await Collect(OpenAi(handler).StreamAsync([User("hi")], default));
        Assert.Equal("Hello", text);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.Equal(handler.Bodies[0], handler.Bodies[1]);
        Assert.Equal(["OpenAI is rate limited, retrying in 0 s…"], status);
    }

    [Fact]
    public async Task Gemini_retries_a_503_as_busy()
    {
        var handler = new Handler(n => n == 1 ? Status(503, retryAfter: TimeSpan.Zero) : Sse(GeminiPart("ok")));
        var (text, status) = await Collect(Gemini(handler, "gemini-3.8-flash", null).StreamAsync([User("hi")], default));
        Assert.Equal("ok", text);
        Assert.Equal(["Gemini API is busy, retrying in 0 s…"], status);
    }

    [Fact]
    public async Task Gives_up_after_three_tries_with_the_rate_limit_error()
    {
        var handler = new Handler(_ => Status(429, retryAfter: TimeSpan.Zero));
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(OpenAi(handler).StreamAsync([User("hi")], default)));
        Assert.Equal(BackendErrorKind.RateLimited, ex.Kind);
        Assert.Contains("slow down", ex.Message);
        Assert.Equal(3, handler.Bodies.Count);
    }

    [Fact]
    public async Task Gemini_bad_key_400_is_still_unauthorized()
    {
        var handler = new Handler(_ => Status(400, """{"error":{"message":"API key not valid. Please pass a valid API key."}}"""));
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(Gemini(handler, "gemini-3.8-flash", null).StreamAsync([User("hi")], default)));
        Assert.Equal(BackendErrorKind.Unauthorized, ex.Kind);
        Assert.Single(handler.Bodies);
    }

    // ---- status notes reach the panel, not the answer -----------------------------------------

    [Fact]
    public async Task Status_deltas_become_status_events_and_stay_out_of_the_answer()
    {
        var events = new List<ChatEvent>();
        var backend = new FakeBackend(ChatDelta.StatusNote("waiting"), new ChatDelta("Hi"));
        var c = new ChatController(_ => backend, null, new ManualTimeProvider(), new FileLog(Path.Combine(_dir, "h.log"))) { BackendId = "fake" };
        c.Event += events.Add;
        await c.SendAsync("q", []);
        Assert.Contains(events, e => e is AssistantStatus { Message: "waiting" });
        Assert.DoesNotContain(events, e => e is AssistantDelta { Text: "" });
        Assert.Equal("Hi", c.Messages[1].Text);
    }
}
