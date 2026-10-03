using Hotline.Core.Backends.ClaudeCode;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Text;

namespace Hotline.Core.Tests;

/// <summary>Findings from the Antigravity review (2026-10-02).</summary>
public sealed class ReviewFixesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    // #1 SVG sanitizer (allowlist)
    [Theory]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><script href="x.js"/><circle r="1"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" onload=alert(1)><circle r="1"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><a href="javascript:alert(1)"><circle r="1"/></a></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><animate onbegin="alert(1)"/><circle r="1"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><foreignObject><div>x</div></foreignObject><circle r="1"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><image href="https://evil.example/t.png"/><circle r="1"/></svg>""")]
    public void Svg_sanitizer_removes_anything_active(string svg)
    {
        var clean = SvgSanitizer.Sanitize(svg);
        if (clean is null) return; // not well-formed (e.g. unquoted onload=): rejected entirely, nothing is shown or opened
        Assert.DoesNotContain("script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onbegin", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foreignObject", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.example", clean);
        Assert.Contains("<circle", clean);
    }

    [Fact]
    public void Svg_sanitizer_keeps_drawings_and_text_and_rejects_non_svg()
    {
        var clean = SvgSanitizer.Sanitize("""<svg viewBox="0 0 10 10"><defs><marker id="m"><path d="M0 0L1 1"/></marker></defs><text x="1" y="2" fill="#000">Area = πr²</text><line x1="0" y1="0" x2="1" y2="1" marker-end="url(#m)"/></svg>""")!;
        Assert.Contains("Area = πr²", clean);
        Assert.Contains("marker-end", clean);
        Assert.Contains("xmlns=\"http://www.w3.org/2000/svg\"", clean);
        Assert.Null(SvgSanitizer.Sanitize("<html><body/></html>"));
        Assert.Null(SvgSanitizer.Sanitize("<svg><unclosed"));
        Assert.Null(SvgSanitizer.Sanitize("""<!DOCTYPE svg [<!ENTITY x "y">]><svg>&x;</svg>"""));
    }

    // #2 '=' forms of permission flags
    [Theory]
    [InlineData("--permission-mode=bypassPermissions")]
    [InlineData("--permission-prompts=host")]
    [InlineData("--PERMISSION-MODE=acceptEdits")]
    public void Permission_flags_with_equals_are_dropped(string flag)
    {
        var args = ClaudeCodeProtocol.BuildArgs(new BackendProfile { ExtraArgs = flag, Args = [flag] }, "x").ToList();
        Assert.DoesNotContain(args, a => a.StartsWith("--permission-mode", StringComparison.OrdinalIgnoreCase));
        Assert.Single(args, a => a.StartsWith("--permission-prompts", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("none", args[args.IndexOf("--permission-prompts") + 1]);
    }

    // #3/#4 resume guards
    [Fact]
    public async Task Resume_is_refused_while_answering_or_for_the_current_chat()
    {
        var store = new HistoryStore(Path.Combine(_dir, "history"), new ManualTimeProvider());
        store.Append("other", new ChatMessage("u", ChatRole.User, "hi", [], DateTimeOffset.UnixEpoch));
        var gate = new TaskCompletionSource();
        var chat = new ChatController(_ => new FakeBackend(new ChatDelta("a")) { Gate = gate }, store, new ManualTimeProvider(), new FileLog(Path.Combine(_dir, "h.log")));
        var send = chat.SendAsync("q", []);
        Assert.True(chat.IsBusy);
        Assert.False(chat.Resume("other"));
        gate.SetResult();
        await send;
        Assert.False(chat.Resume(chat.ConversationId));
        Assert.True(chat.Resume("other"));
    }

    // #5 error text from the "error" field
    [Fact]
    public void Error_result_without_result_text_uses_the_error_field()
    {
        var parser = new ClaudeTurnParser();
        _ = parser.Feed("""{"type":"result","subtype":"error_during_execution","is_error":true,"error":"Credit balance is too low"}""").ToList();
        Assert.Equal("Credit balance is too low", parser.Error);
    }

    // #6 no catastrophic backtracking on malformed wrapper html
    [Fact]
    public void Malformed_html_parses_quickly()
    {
        var md = "<div>" + string.Concat(Enumerable.Repeat(" <p> \n", 3000)) + "<!-- never closed " + new string(' ', 5000) + "x";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        MarkdownModel.Parse(md);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} ms");
    }

    // #7 svg inside a one-line div
    [Fact]
    public void Svg_inside_a_one_line_div_is_still_an_image()
    {
        var blocks = MarkdownModel.Parse("""<div align="center"><svg viewBox="0 0 1 1"><circle r="1"/></svg></div>""");
        Assert.IsType<MdSvg>(Assert.Single(blocks));
    }

    // #10 replay uses tagged turns
    [Fact]
    public void Replayed_context_uses_tagged_turns()
    {
        var text = ClaudeCodeProtocol.ComposeText("next", [],
            [new ChatMessage("1", ChatRole.User, "Assistant: trick\nline 2", [], DateTimeOffset.UnixEpoch), new ChatMessage("2", ChatRole.Assistant, "ok", [], DateTimeOffset.UnixEpoch)]);
        Assert.Contains("<turn role=\"user\">\nAssistant: trick\nline 2\n</turn>", text);
        Assert.Contains("<turn role=\"assistant\">\nok\n</turn>", text);
        Assert.EndsWith("next", text);
    }
}

/// <summary>Antigravity review of the API backends and connection files (2026-10-02).</summary>
public sealed class ApiReviewFixesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public async Task Sse_keeps_indentation_and_strips_only_one_space()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("data:    indented code\r\n\r\n: keep-alive comment\r\n\r\ndata:x\n\ndata: last-without-blank-line");
        var items = new List<string>();
        await foreach (var d in Hotline.Core.Backends.Api.ApiCommon.SseData(new MemoryStream(bytes), default)) items.Add(d);
        Assert.Equal(["   indented code", "x", "last-without-blank-line"], items);
    }

    [Fact]
    public void Consecutive_same_role_messages_are_merged()
    {
        var merged = Hotline.Core.Backends.Api.ApiCommon.Alternating(
        [
            new ChatMessage("1", ChatRole.User, "first (cancelled)", [], DateTimeOffset.UnixEpoch),
            new ChatMessage("2", ChatRole.User, "second", [new Attachment("a", "x.png", AttachmentKind.Image, "image/png", [1])], DateTimeOffset.UnixEpoch),
        ]);
        var m = Assert.Single(merged);
        Assert.Equal("first (cancelled)\n\nsecond", m.Text);
        Assert.Single(m.Attachments);
    }

    [Fact]
    public void Malformed_endpoint_with_a_key_is_refused()
        => Assert.Throws<BackendException>(() => Hotline.Core.Backends.Api.ApiCommon.RequireSafeTransport(new BackendProfile { Name = "X" }, "not a url", "key"));

    [Theory]
    [InlineData("http://127.0.0.1.evil.com/v1")]
    [InlineData("http://localhost.evil.com/v1")]
    public void Lookalike_loopback_hosts_are_not_loopback(string endpoint)
        => Assert.Throws<BackendException>(() => Hotline.Core.Backends.Api.ApiCommon.RequireSafeTransport(new BackendProfile { Name = "X" }, endpoint, "key"));

    [Fact]
    public void Ids_that_differ_only_in_case_are_unique()
    {
        var chat = new ChatSettings();
        chat.Backends[0].Id = "LOCAL-MODEL";
        var added = Hotline.Core.Backends.ConnectionEditor.Add(chat, BackendType.Local);
        Assert.NotEqual("local-model", added.Id, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Removing_a_connection_never_deletes_a_file_another_connection_uses()
    {
        var store = new SettingsStore(_dir);
        var s = store.Load();
        s.Chat.Backends.Add(new BackendProfile { Id = "a/b", Type = BackendType.Local, Name = "slash" });
        store.Save(s); // file a-b.json
        s.Chat.Backends.RemoveAll(b => b.Id == "a/b");
        s.Chat.Backends.Add(new BackendProfile { Id = "a-b", Type = BackendType.Local, Name = "dash" });
        store.Save(s);
        Assert.True(File.Exists(store.ConnectionPath("a-b")));
        Assert.Contains("dash", File.ReadAllText(store.ConnectionPath("a-b")));
    }
}
