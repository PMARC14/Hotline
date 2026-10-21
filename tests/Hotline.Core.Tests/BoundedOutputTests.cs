using Hotline.Core.Chat;
using Hotline.Core.Processes;

namespace Hotline.Core.Tests;

/// <summary>CLI output is untrusted: a line (or a whole output) can't grow without bound.</summary>
public class BoundedOutputTests
{
    private static async Task<List<string>> AllLines(string text, int max = 100)
    {
        var reader = new BoundedLineReader(new StringReader(text), max);
        var lines = new List<string>();
        while (await reader.ReadLineAsync(TestContext.Current.CancellationToken) is { } line) lines.Add(line);
        return lines;
    }

    [Theory]
    [InlineData("a\nb\n", new[] { "a", "b" })]
    [InlineData("a\r\nb\rc", new[] { "a", "b", "c" })]
    [InlineData("\n\r\n", new[] { "", "" })]
    [InlineData("", new string[0])]
    [InlineData("last", new[] { "last" })]
    public async Task Reads_lines_like_stream_reader(string text, string[] expected)
    {
        Assert.Equal(expected, await AllLines(text));
        // Same as the reader it replaces.
        var plain = new StringReader(text);
        var reference = new List<string>();
        while (await plain.ReadLineAsync(TestContext.Current.CancellationToken) is { } line) reference.Add(line);
        Assert.Equal(reference, expected);
    }

    [Fact]
    public async Task Lines_longer_than_the_buffer_are_joined()
    {
        var longLine = new string('x', 10_000);
        Assert.Equal([longLine, "y"], await AllLines(longLine + "\r\ny", max: 20_000));
    }

    [Fact]
    public async Task A_line_over_the_limit_throws_instead_of_growing()
    {
        var reader = new BoundedLineReader(new StringReader("ok\n" + new string('x', 10_001)), 10_000);
        Assert.Equal("ok", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OutputTooLongException>(() => reader.ReadLineAsync(TestContext.Current.CancellationToken));
        // Exactly at the limit is fine.
        Assert.Equal(new string('x', 10), await new BoundedLineReader(new StringReader(new string('x', 10) + "\n"), 10).ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Read_to_end_is_capped()
    {
        Assert.Equal("abc\ndef", await BoundedLineReader.ReadToEndAsync(new StringReader("abc\ndef"), 7, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OutputTooLongException>(() => BoundedLineReader.ReadToEndAsync(new StringReader(new string('x', 9000)), 8000, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_cli_printing_an_endless_line_is_stopped_with_a_clear_error()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var p = new SystemLineProcess("powershell.exe",
            ["-NoProfile", "-Command", "[Console]::Out.Write('x' * 50000); [Console]::Out.Flush(); Start-Sleep 60"], Path.GetTempPath(), null, maxLineChars: 1000);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ex = await Assert.ThrowsAsync<BackendException>(() => p.ReadLineAsync(cts.Token));
        Assert.Equal(BackendErrorKind.Failed, ex.Kind);
        Assert.Contains("powershell", ex.Message);
        Assert.True(p.HasExited); // killed, so the next turn starts a fresh process
    }
}
