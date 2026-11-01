using Hotline.Core.Diagnostics;

namespace Hotline.Core.Tests;

public sealed class LogTextTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Line_breaks_are_indented_so_a_message_cannot_forge_a_log_line()
    {
        var clean = LogText.Clean("failed\r\n2026-10-21T10:00:00 INFO key accepted\nnext");
        Assert.DoesNotContain("\r", clean);
        Assert.All(clean.Split('\n').Skip(1), line => Assert.StartsWith("    ", line));
    }

    [Fact]
    public void Control_characters_are_replaced()
    {
        var clean = LogText.Clean("a\u001b[31mred\u0000b\u0007c\u007fd\ttab");
        Assert.DoesNotContain('\u001b', clean);
        Assert.DoesNotContain('\0', clean);
        Assert.DoesNotContain('\u0007', clean);
        Assert.DoesNotContain('\u007f', clean);
        Assert.Contains("tab", clean);
    }

    [Theory]
    [InlineData("Authorization: Bearer abcdef1234567890abcdef", "abcdef1234567890abcdef")]
    [InlineData("x-api-key=sk-ant-api03-ABCDEFGHIJKLMNOPQRSTUV", "ABCDEFGHIJKLMNOPQRSTUV")]
    [InlineData("GET https://x.test/v1/models?key=AIzaSyA1234567890abcdefghijk&alt=sse", "AIzaSyA1234567890abcdefghijk")]
    [InlineData("\"apiKey\": \"sk-proj-0123456789abcdefghij\"", "sk-proj-0123456789abcdefghij")]
    [InlineData("token=ghp_0123456789abcdefghijklmnop", "ghp_0123456789abcdefghijklmnop")]
    [InlineData("password: hunter2hunter2", "hunter2hunter2")]
    [InlineData("https://user:s3cretpass@example.test/path", "s3cretpass")]
    public void Secrets_are_redacted(string text, string secret)
    {
        var clean = LogText.Clean(text);
        Assert.DoesNotContain(secret, clean);
        Assert.Contains("[redacted]", clean);
    }

    [Theory]
    [InlineData("chat send via claude: 120 chars, 0 attachment(s)")]
    [InlineData("selection: 54 chars via uia in 12 ms")]
    [InlineData("agy started: C:/x/agy.exe --model gemini-3.8-flash-high --conversation abc123")]
    [InlineData("a token budget of 4000 tokens")]
    public void Ordinary_lines_are_left_alone(string text) => Assert.Equal(text, LogText.Clean(text));

    [Fact]
    public void A_huge_message_is_cut_with_a_note()
    {
        var clean = LogText.Clean(new string('x', 100_000));
        Assert.True(clean.Length < 17_000);
        Assert.Contains("truncated", clean);
    }

    [Fact]
    public void Null_and_empty_are_fine()
    {
        Assert.Equal("", LogText.Clean(null));
        Assert.Equal("", LogText.Clean(""));
    }

    [Theory]
    [InlineData("hotline-action://ask?text=my%20private%20words&x=1", "hotline-action://ask")]
    [InlineData("hotline://key?state=Tap", "hotline://key")]
    [InlineData("hotline-action://run/translate?text=secret", "hotline-action://run/translate")]
    public void Uris_are_logged_without_their_query(string uri, string expected)
    {
        var shown = LogText.SafeUri(new Uri(uri));
        Assert.StartsWith(expected, shown);
        Assert.DoesNotContain("secret", shown);
        Assert.DoesNotContain("private", shown);
    }

    [Fact]
    public void Uri_null_is_fine() => Assert.Equal("(none)", LogText.SafeUri(null));

    [Fact]
    public void FileLog_applies_the_cleaning_to_every_level()
    {
        var path = Path.Combine(_dir, "hotline.log");
        var log = new FileLog(path) { Verbose = true };
        log.Info("one\nINFO forged");
        log.Debug("Authorization: Bearer abcdef1234567890abcdef");
        log.Error("failed key=AIzaSyA1234567890abcdefghijk", new InvalidOperationException("line1\nline2 token=ghp_0123456789abcdefghijklmnop"));
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("abcdef1234567890abcdef", text);
        Assert.DoesNotContain("AIzaSyA1234567890abcdefghijk", text);
        Assert.DoesNotContain("ghp_0123456789abcdefghijklmnop", text);
        Assert.All(text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith("    ")),
            line => Assert.Matches(@"^\d{4}-\d\d-\d\dT[\d:.]+[+-]\d\d:\d\d (INFO|DEBUG|ERROR) ", line));
    }
}
