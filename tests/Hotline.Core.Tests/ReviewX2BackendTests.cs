using System.Net;
using System.Text;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Api;
using Hotline.Core.Backends.ClaudeCode;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Processes;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Fixes from the full-codebase review: SSE tail, odd JSON from proxies, Claude Code start-up failures.</summary>
public sealed class ReviewX2BackendTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static async Task<List<string>> Events(string sse)
    {
        var items = new List<string>();
        await foreach (var d in ApiCommon.SseData(new MemoryStream(Encoding.UTF8.GetBytes(sse)), default)) items.Add(d);
        return items;
    }

    [Theory]
    [InlineData("data: a\n", "a")]                  // last event ended by one newline then EOF is not dropped
    [InlineData("data: a\n\ndata: b\n", "a|b")]
    [InlineData("data: a", "a")]                    // no newline at all is kept
    [InlineData("data: a\n\n", "a")]                // ended by a blank line is not repeated at EOF
    public async Task Sse_events_at_the_end_of_the_stream(string sse, string expected)
        => Assert.Equal(expected.Split('|'), await Events(sse));

    // ---- odd JSON shapes from proxies end up as skipped chunks, not InvalidOperationException ----

    private static async Task<string> Run(BackendProfile p, params string[] data)
    {
        var handler = new FixedHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Concat(data.Select(d => $"data: {d}\n\n")), Encoding.UTF8, "text/event-stream"),
        });
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretKeys.ApiKey(p.Id), "k");
        var log = new FileLog(Path.Combine(Path.GetTempPath(), "hotline-tests-x2.log"));
        IChatBackend b = p.Type == BackendType.Gemini
            ? new GeminiBackend(p, new HttpClient(handler), secrets, _ => "", log)
            : new OpenAiBackend(p, new HttpClient(handler), secrets, _ => "", log);
        var text = "";
        await foreach (var d in b.StreamAsync([new("1", ChatRole.User, "hi", [], Now)], default)) text += d.Text;
        return text;
    }

    private static Task<string> RunGemini(params string[] data)
        => Run(new BackendProfile { Id = "g", Name = "G", Type = BackendType.Gemini, Model = "gemini-3.8-flash" }, data);

    private static Task<string> RunOpenAi(params string[] data)
        => Run(new BackendProfile { Id = "o", Name = "O", Type = BackendType.OpenAiCompatible, Model = "m", Endpoint = "https://api.example.com/v1" }, data);

    private sealed class FixedHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond());
    }

    [Fact]
    public async Task Gemini_skips_chunks_that_are_not_the_expected_shape()
    {
        var text = await RunGemini("[1,2]", "\"str\"", "{\"candidates\":5}", "{\"candidates\":[{\"content\":{\"parts\":{}}}]}",
            "{\"promptFeedback\":7}", "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":5},{\"text\":\"ok\"}]}}]}");
        Assert.Equal("ok", text);
    }

    [Fact]
    public async Task OpenAi_skips_chunks_whose_root_is_not_an_object()
    {
        var text = await RunOpenAi("[1]", "42", "\"x\"", "{\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}", "[DONE]");
        Assert.Equal("hi", text);
    }

    // ---- Claude Code ----

    private ClaudeCodeBackend Claude(ILineProcessFactory factory, bool keep = true) =>
        new(new BackendProfile { Id = "cc", Name = "Claude", Type = BackendType.ClaudeCode, KeepCliSessions = keep }, () => @"C:\claude.exe",
            factory, new FileLog(Path.Combine(_dir, "h.log")), _ => "SYSTEM", Path.Combine(_dir, "home"));

    [Fact]
    public async Task Claude_exiting_at_once_reports_its_error_instead_of_a_broken_pipe()
    {
        var factory = new FakeLineProcessFactory
        {
            Create = () =>
            {
                var p = new FakeLineProcess { StandardErrorTail = "Invalid API key - Please run /login", WriteFailure = new IOException("The pipe is being closed.") };
                p.Exit(); // already gone: reads end at once
                return p;
            },
        };
        var b = Claude(factory);
        var ex = await Assert.ThrowsAsync<BackendException>(async () =>
        {
            await foreach (var _ in b.StreamAsync([new("1", ChatRole.User, "hi", [], Now)], default)) { }
        });
        Assert.NotEqual(BackendErrorKind.Failed, ex.Kind); // mapped from the stderr tail (signed out)
    }

    [Fact]
    public async Task Claude_without_kept_sessions_does_not_report_a_session_it_cannot_resume()
    {
        var factory = new FakeLineProcessFactory
        {
            Create = () => new FakeLineProcess
            {
                Respond = _ =>
                [
                    "{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"6a8b2362-20cc-4208-bd05-cf9b25fe01c9\",\"tools\":[]}",
                    "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"ok\",\"session_id\":\"6a8b2362-20cc-4208-bd05-cf9b25fe01c9\"}",
                ],
            },
        };
        var b = Claude(factory, keep: false);
        var sessions = new List<CliSession>();
        await foreach (var d in b.StreamAsync([new("1", ChatRole.User, "hi", [], Now)], default))
            if (d.Session is { } s) sessions.Add(s);
        Assert.Empty(sessions);
    }
}
