using System.Text.Json;
using Hotline.Core.Chat;
using Hotline.Core.Tools;

namespace Hotline.Core.Tests;

/// <summary>One scripted MCP session fake for tool tests: tools it lists, call tracking, failure injection, delays and dynamic read-only flags.</summary>
public sealed class FakeMcpSession(params (string Name, bool ReadOnly)[] tools) : IMcpSession
{
    public int Lists;
    public int Calls => Called.Count;
    public List<string> Called { get; } = [];
    public bool Disposed;
    public bool Throw;
    public TimeSpan ListDelay;
    public Func<bool>? ReadOnlyNow;

    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct)
    {
        Lists++;
        if (ListDelay > TimeSpan.Zero) await Task.Delay(ListDelay, ct);
        return tools.Select(t => new McpToolInfo(t.Name, $"does {t.Name}", JsonDocument.Parse("""{"type":"object"}""").RootElement, ReadOnlyNow?.Invoke() ?? t.ReadOnly)).ToList();
    }

    public Task<ToolResult> CallAsync(string tool, JsonElement args, CancellationToken ct)
    {
        Called.Add(tool);
        return Throw ? throw new IOException("pipe closed") : Task.FromResult(new ToolResult($"{tool} done", false));
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Shared helpers for the backend tests: a recording HTTP fake, a stream collector and message factories.</summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _respond;

    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = (r, _) => respond(r);
    public RecordingHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) => _respond = respond;

    public List<(HttpRequestMessage Request, string Body)> Seen { get; } = [];
    public IReadOnlyList<HttpRequestMessage> Requests => [.. Seen.Select(s => s.Request)];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Seen.Add((request, body));
        return _respond(request, body);
    }
}

public static class TestStreams
{
    /// <summary>The text a stream shows at the end, honouring ResetBefore.</summary>
    public static async Task<string> Collect(IAsyncEnumerable<ChatDelta> s)
    {
        var text = "";
        await foreach (var d in s) text = d.ResetBefore ? d.Text : text + d.Text;
        return text;
    }
}

public static class TestMsg
{
    public static ChatMessage U(string id, string text, params Attachment[] a) => new(id, ChatRole.User, text, a, DateTimeOffset.UnixEpoch);
    public static ChatMessage A(string id, string text) => new(id, ChatRole.Assistant, text, [], DateTimeOffset.UnixEpoch);
}
