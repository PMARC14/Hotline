using static Hotline.Core.Tests.TestStreams;
using System.Net;
using System.Text;
using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Api;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class GeminiBackendTests : TempDirTest
{

    private static string Part(string text, bool thought = false) => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { role = "model", parts = new[] { thought ? (object)new { text, thought = true } : new { text } } } } },
    });

    private static HttpResponseMessage Sse(params string[] data) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Concat(data.Select(d => $"data: {d}\r\n\r\n")), Encoding.UTF8, "text/event-stream"),
    };

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private (GeminiBackend, RecordingHandler) New(BackendProfile p, Func<HttpRequestMessage, HttpResponseMessage> respond, string? key = "AIza-test")
    {
        var handler = new RecordingHandler(respond);
        var secrets = new InMemorySecretStore();
        if (key is not null) secrets.Set(SecretKeys.ApiKey(p.Id), key);
        return (new GeminiBackend(p, new HttpClient(handler), secrets, _ => "Be brief.", new FileLog(Path.Combine(_dir, "h.log"))), handler);
    }

    private static BackendProfile Gemini(string? model = "gemini-3.8-flash") => new() { Id = "g", Name = "Gemini API", Type = BackendType.Gemini, Model = model };

    [Fact]
    public async Task MAX_TOKENS_keeps_plain_answer_and_yields_a_localized_status_note()
    {
        var (backend, _) = New(Gemini(), _ => Sse(Part("Partial answer."), """{"candidates":[{"finishReason":"MAX_TOKENS"}]}"""));
        var deltas = new List<ChatDelta>();
        await foreach (var delta in backend.StreamAsync([new("1", ChatRole.User, "hi", [], Now)], default)) deltas.Add(delta);
        Assert.Equal("Partial answer.", string.Concat(deltas.Select(d => d.Text)));
        var status = Assert.Single(deltas, d => d.Status is not null);
        Assert.Equal(Hotline.Core.Text.Localization.L.F(Hotline.Core.Text.Localization.Strings.Api_StoppedAnswer, "Gemini API", "MAX_TOKENS"), status.Status);
    }

    [Fact]
    public async Task Streams_generate_content_with_roles_system_and_images_and_skips_thoughts()
    {
        var (b, handler) = New(Gemini(), _ => Sse(Part("thinking…", thought: true), Part("Hel"), Part("lo")));
        var img = new Attachment("a", "x.jpg", AttachmentKind.Image, "image/jpeg", [7]);
        var text = await Collect(b.StreamAsync(
            [new("1", ChatRole.User, "hi", [], Now), new("2", ChatRole.Assistant, "hey", [], Now), new("3", ChatRole.User, "look", [img], Now)], default));
        Assert.Equal("Hello", text);

        var (request, body) = Assert.Single(handler.Seen);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:streamGenerateContent?alt=sse", request.RequestUri!.ToString());
        Assert.Equal("AIza-test", request.Headers.GetValues("x-goog-api-key").Single());
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Be brief.", json.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        var contents = json.GetProperty("contents").EnumerateArray().ToList();
        Assert.Equal(["user", "model", "user"], contents.Select(c => c.GetProperty("role").GetString()));
        var inline = contents[2].GetProperty("parts")[1].GetProperty("inlineData");
        Assert.Equal("image/jpeg", inline.GetProperty("mimeType").GetString());
        Assert.Equal(Convert.ToBase64String(new byte[] { 7 }), inline.GetProperty("data").GetString());
    }

    [Fact]
    public async Task Needs_a_model_and_a_key()
    {
        var (b, _) = New(Gemini(model: null), _ => Sse());
        Assert.Contains("model", (await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([new("1", ChatRole.User, "hi", [], Now)], default)))).Message);
        var (b2, h2) = New(Gemini(), _ => Sse(), key: null);
        Assert.Equal(BackendErrorKind.NotConfigured, (await Assert.ThrowsAsync<BackendException>(() => Collect(b2.StreamAsync([new("1", ChatRole.User, "hi", [], Now)], default)))).Kind);
        Assert.Empty(h2.Seen);
    }

    [Fact]
    public async Task Blocked_prompt_is_reported()
    {
        var (b, _) = New(Gemini(), _ => Sse("""{"promptFeedback":{"blockReason":"SAFETY"}}"""));
        Assert.Contains("SAFETY", (await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([new("1", ChatRole.User, "hi", [], Now)], default)))).Message);
    }

    [Fact]
    public async Task Bad_key_is_unauthorized_with_googles_message()
    {
        var (b, _) = New(Gemini(), _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""[{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT"}}]"""),
        });
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(b.StreamAsync([new("1", ChatRole.User, "hi", [], Now)], default)));
        Assert.Equal(BackendErrorKind.Unauthorized, ex.Kind);
        Assert.Contains("API key not valid", ex.Message);
    }
}
