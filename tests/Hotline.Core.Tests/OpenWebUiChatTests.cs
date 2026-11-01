using System.Net;
using System.Text.Json.Nodes;
using Hotline.Core.Backends;
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

/// <summary>Continue in › Open WebUI: a real chat with the whole conversation, through its API.</summary>
public class OpenWebUiChatTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private static readonly ChatMessage[] Chat =
    [
        new("1", ChatRole.User, "hi", [new Attachment("a", "notes.txt", AttachmentKind.Text, "text/plain", [])], DateTimeOffset.FromUnixTimeSeconds(1000)),
        new("2", ChatRole.Assistant, "hello", [], DateTimeOffset.FromUnixTimeSeconds(1001)),
        new("3", ChatRole.User, "more", [], DateTimeOffset.FromUnixTimeSeconds(1002)),
    ];

    private static JsonObject Body()
    {
        var ids = new Queue<Guid>([Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Guid.Parse("00000000-0000-0000-0000-000000000003")]);
        return OpenWebUiChat.Body("hi", "llama3", Chat, ids.Dequeue);
    }

    [Fact]
    public void Body_chains_every_message_in_order()
    {
        var chat = Body()["chat"]!;
        Assert.Equal("hi", (string?)chat["title"]);
        Assert.Equal("llama3", (string?)chat["models"]![0]);
        var messages = chat["messages"]!.AsArray();
        Assert.Equal(3, messages.Count);
        Assert.Equal(["user", "assistant", "user"], messages.Select(m => (string?)m!["role"]));
        Assert.Null(messages[0]!["parentId"]);
        Assert.Equal((string?)messages[0]!["id"], (string?)messages[1]!["parentId"]);
        Assert.Equal((string?)messages[2]!["id"], (string?)messages[1]!["childrenIds"]![0]);
        Assert.Equal("hi\n[attached: notes.txt]", (string?)messages[0]!["content"]);
        Assert.Equal(1000, (long)messages[0]!["timestamp"]!);
        var history = chat["history"]!;
        Assert.Equal((string?)messages[2]!["id"], (string?)history["currentId"]);
        Assert.Equal(3, history["messages"]!.AsObject().Count);
    }

    [Fact]
    public async Task Creates_the_chat_and_returns_its_page()
    {
        HttpRequestMessage? seen = null;
        var handler = new Handler(r => { seen = r; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"abc\"}") }; });
        var page = await OpenWebUiChat.CreateAsync(new HttpClient(handler), new Uri("http://localhost:3000"), "k", Body(), CancellationToken.None);
        Assert.Equal(new Uri("http://localhost:3000/c/abc"), page);
        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal(new Uri("http://localhost:3000/api/v1/chats/new"), seen.RequestUri);
        Assert.Equal("Bearer k", seen.Headers.Authorization!.ToString());
    }

    [Theory]
    [InlineData("http://localhost:3000", 401, "", OpenWebUiError.KeyRefused, 1)]
    [InlineData("http://localhost:3000", 403, "", OpenWebUiError.KeyRefused, 1)]
    [InlineData("http://localhost:3000", null, null, OpenWebUiError.Unreachable, 1)]
    [InlineData("http://192.168.1.5:3000", 200, "", OpenWebUiError.InsecureKey, 0)]
    [InlineData("http://localhost:3000", 200, "true", OpenWebUiError.BadReply, 1)]
    [InlineData("http://localhost:3000", 200, "{}", OpenWebUiError.BadReply, 1)]
    public async Task Creation_reports_errors_and_never_sends_a_key_to_an_insecure_remote(
        string url, int? status, string? body, OpenWebUiError expected, int expectedCalls)
    {
        var handler = new Handler(_ => status is { } code
            ? new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(body!) }
            : throw new HttpRequestException("refused"));
        var e = await Assert.ThrowsAsync<OpenWebUiException>(() => OpenWebUiChat.CreateAsync(
            new HttpClient(handler), new Uri(url), "k", Body(), CancellationToken.None));
        Assert.Equal(expected, e.Error);
        Assert.Equal(expectedCalls, handler.Calls);
    }
}
