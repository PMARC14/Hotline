using System.Net;
using System.Text;
using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Api;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Answer length, temperature and effort reach each API the way it documents them.</summary>
public sealed class ConnectionOptionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private sealed class Capture : HttpMessageHandler
    {
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            var data = request.RequestUri!.Host.Contains("googleapis")
                ? """{"candidates":[{"content":{"role":"model","parts":[{"text":"ok"}]}}]}"""
                : """{"choices":[{"delta":{"content":"ok"}}]}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"data: {data}\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }

    private static readonly ChatMessage Hi = new("1", ChatRole.User, "hi", [], DateTimeOffset.UnixEpoch);

    private async Task<JsonElement> Send(BackendProfile p)
    {
        var capture = new Capture();
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretKeys.ApiKey(p.Id), "key");
        var log = new FileLog(Path.Combine(_dir, "h.log"));
        IChatBackend backend = p.Type == BackendType.Gemini
            ? new GeminiBackend(p, new HttpClient(capture), secrets, _ => "", log)
            : new OpenAiBackend(p, new HttpClient(capture), secrets, _ => "", log);
        await foreach (var _ in backend.StreamAsync([Hi], default)) { }
        return JsonDocument.Parse(capture.Body).RootElement;
    }

    [Fact]
    public async Task OpenAi_gets_max_completion_tokens_and_temperature()
    {
        var body = await Send(new BackendProfile { Id = "oa", Type = BackendType.OpenAiCompatible, Endpoint = "https://api.openai.com/v1", Model = "gpt-x", MaxTokens = 2000, Temperature = 0.3 });
        Assert.Equal(2000, body.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(body.TryGetProperty("max_tokens", out _));
        Assert.Equal(0.3, body.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task Other_openai_style_servers_get_max_tokens()
    {
        var body = await Send(new BackendProfile { Id = "or", Type = BackendType.OpenAiCompatible, Endpoint = "https://openrouter.ai/api/v1", Model = "m", MaxTokens = 500 });
        Assert.Equal(500, body.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task A_reasoning_effort_drops_the_temperature()
    {
        var body = await Send(new BackendProfile { Id = "oa", Type = BackendType.OpenAiCompatible, Endpoint = "https://api.openai.com/v1", Model = "o-x", Effort = "high", Temperature = 0.2 });
        Assert.Equal("high", body.GetProperty("reasoning_effort").GetString());
        Assert.False(body.TryGetProperty("temperature", out _)); // reasoning models only accept the default
    }

    [Fact]
    public async Task Local_effort_also_goes_to_the_chat_template_for_llama_server()
    {
        var body = await Send(new BackendProfile { Id = "lc", Type = BackendType.Local, Endpoint = "http://127.0.0.1:8080/v1", Effort = "medium", MaxTokens = 4096 });
        Assert.Equal("medium", body.GetProperty("reasoning_effort").GetString());
        Assert.Equal("medium", body.GetProperty("chat_template_kwargs").GetProperty("reasoning_effort").GetString());
        Assert.Equal(4096, body.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task Local_reasoning_models_keep_their_temperature_and_azure_counts_as_openai()
    {
        var local = await Send(new BackendProfile { Id = "lc", Type = BackendType.Local, Endpoint = "http://127.0.0.1:8080/v1", Effort = "high", Temperature = 0.6 });
        Assert.Equal(0.6, local.GetProperty("temperature").GetDouble());
        var azure = await Send(new BackendProfile { Id = "az", Type = BackendType.OpenAiCompatible, Endpoint = "https://me.openai.azure.com/openai/v1", Model = "m", MaxTokens = 100 });
        Assert.Equal(100, azure.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task Nothing_extra_is_sent_by_default()
    {
        var body = await Send(new BackendProfile { Id = "lc", Type = BackendType.Local, Endpoint = "http://127.0.0.1:8080/v1" });
        foreach (var name in new[] { "max_tokens", "max_completion_tokens", "temperature", "reasoning_effort", "chat_template_kwargs" })
            Assert.False(body.TryGetProperty(name, out _), name);
    }

    [Fact]
    public async Task Gemini_gets_max_output_tokens_and_temperature_next_to_thinking()
    {
        var body = await Send(new BackendProfile { Id = "g", Type = BackendType.Gemini, Model = "gemini-3.8-flash", Effort = "low", MaxTokens = 8192, Temperature = 1.0 });
        var config = body.GetProperty("generationConfig");
        Assert.Equal(8192, config.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(1.0, config.GetProperty("temperature").GetDouble());
        Assert.Equal("low", config.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
    }

    [Fact]
    public void Claude_api_offers_no_temperature_but_every_api_offers_answer_length()
    {
        Assert.False(ConnectionTypes.Of(BackendType.Anthropic).Has(ConnectionField.Temperature)); // rejected while it thinks
        foreach (var t in new[] { BackendType.Anthropic, BackendType.Gemini, BackendType.OpenAiCompatible, BackendType.Local })
            Assert.True(ConnectionTypes.Of(t).Has(ConnectionField.MaxTokens), t.ToString());
        Assert.False(ConnectionTypes.Of(BackendType.ClaudeCode).Has(ConnectionField.MaxTokens));
    }

    [Fact]
    public void Out_of_range_values_are_corrected_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName),
            """{ "chat": { "defaultBackend": "x", "backends": [ { "id": "x", "type": "local", "name": "x", "maxTokens": 0, "temperature": 9 } ] } }""");
        var p = Assert.Single(new SettingsStore(_dir).Load().Chat.Backends, b => b.Id == "x");
        Assert.Null(p.MaxTokens);
        Assert.Equal(2, p.Temperature);
    }
}
