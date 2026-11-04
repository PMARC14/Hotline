using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

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
