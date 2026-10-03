using System.Net;
using System.Text;
using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Api;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Tools;

namespace Hotline.Core.Tests;

/// <summary>Tool calling in the API backends (OpenAI-compatible, Gemini, Anthropic) with a fake tool host.</summary>
public sealed class ToolLoopTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    internal sealed class FakeToolHost : IToolHost
    {
        public List<(string Name, string Args)> Calls { get; } = [];
        public Func<string, ToolResult> Result { get; set; } = name => new ToolResult($"{name} result", false);
        public Task<IReadOnlyList<ToolSpec>> GetToolsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ToolSpec>>(
        [
            new ToolSpec("files__read_file", "files", "read_file", "Reads a file", JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""").RootElement, true),
        ]);
        public Task<ToolResult> CallAsync(string apiName, JsonElement arguments, CancellationToken ct)
        {
            Calls.Add((apiName, arguments.GetRawText()));
            return Task.FromResult(Result(apiName));
        }
    }

    private sealed class Handler(Func<int, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Bodies.Add(body);
            return respond(Bodies.Count, body);
        }
    }

    private static HttpResponseMessage Sse(params string[] data) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Concat(data.Select(d => $"data: {d}\n\n")), Encoding.UTF8, "text/event-stream"),
    };

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static ChatMessage U(string text) => new("u1", ChatRole.User, text, [], Now);

    private static async Task<string> Collect(IAsyncEnumerable<ChatDelta> s)
    {
        var text = "";
        await foreach (var d in s) text = d.ResetBefore ? d.Text : text + d.Text;
        return text;
    }

    private FileLog Log => new(Path.Combine(_dir, "h.log"));

    // ---- OpenAI-compatible -------------------------------------------------------------------

    private static string ToolCallChunk(int index, string? id, string? name, string args) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { delta = new { tool_calls = new[] { new { index, id, type = id is null ? null : "function", function = new { name, arguments = args } } } } } },
    });

    private static string TextChunk(string text) => JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = text } } } });

    [Fact]
    public async Task OpenAi_runs_streamed_tool_calls_and_feeds_results_back()
    {
        var host = new FakeToolHost();
        var handler = new Handler((n, _) => n == 1
            ? Sse(TextChunk("Let me look."), ToolCallChunk(0, "call_1", "files__read_file", "{\"pa"), ToolCallChunk(0, null, null, "th\":\"a.txt\"}"),
                  """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""", "[DONE]")
            : Sse(TextChunk("It says hi."), "[DONE]"));
        var b = new OpenAiBackend(new BackendProfile { Id = "l", Name = "Local", Type = BackendType.Local, Tools = ToolMode.Inherit },
            new HttpClient(handler), new InMemorySecretStore(), _ => "", Log, host);

        var text = await Collect(b.StreamAsync([U("what's in a.txt?")], default));

        Assert.Equal(("files__read_file", """{"path":"a.txt"}"""), Assert.Single(host.Calls));
        Assert.Contains("Let me look.", text);
        Assert.Contains("files", text);       // a visible note about the tool call
        Assert.EndsWith("It says hi.", text);
        var first = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        Assert.Equal("files__read_file", first.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        var second = JsonDocument.Parse(handler.Bodies[1]).RootElement.GetProperty("messages").EnumerateArray().ToList();
        var assistant = second[^2];
        Assert.Equal("call_1", assistant.GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal(("tool", "call_1", "files__read_file result"),
            (second[^1].GetProperty("role").GetString(), second[^1].GetProperty("tool_call_id").GetString(), second[^1].GetProperty("content").GetString()));
    }

    [Fact]
    public async Task OpenAi_chat_only_connections_get_no_tools()
    {
        var handler = new Handler((_, _) => Sse(TextChunk("hi"), "[DONE]"));
        var b = new OpenAiBackend(new BackendProfile { Id = "l", Type = BackendType.Local }, new HttpClient(handler), new InMemorySecretStore(), _ => "", Log, new FakeToolHost());
        await Collect(b.StreamAsync([U("hi")], default));
        Assert.False(JsonDocument.Parse(handler.Bodies[0]).RootElement.TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task OpenAi_stops_after_the_round_limit()
    {
        var host = new FakeToolHost();
        var handler = new Handler((_, _) => Sse(ToolCallChunk(0, "c", "files__read_file", "{}"), """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""", "[DONE]"));
        var b = new OpenAiBackend(new BackendProfile { Id = "l", Type = BackendType.Local, Tools = ToolMode.Inherit }, new HttpClient(handler), new InMemorySecretStore(), _ => "", Log, host);
        var text = await Collect(b.StreamAsync([U("loop forever")], default));
        Assert.Equal(ToolLoop.MaxRounds, host.Calls.Count);
        Assert.Contains("Stopped", text);
    }
}
