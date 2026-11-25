using Hotline.Core.Tools;

namespace Hotline.Core.Tests;

public sealed class StderrPumpTests
{
    [Fact]
    public async Task A_server_writing_without_newlines_gives_capped_lines()
    {
        var lines = new List<string>();
        var done = new TaskCompletionSource();
        var text = "first\r\n" + new string('x', 1_000_000) + "\nlast";
        var reader = new StringReader(text);
        _ = StdioMcpSession.PumpStderr(reader, l => { lock (lines) { lines.Add(l); if (l == "last") done.TrySetResult(); } });
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(["first", new string('x', 4000), "last"], lines);
    }

    // A server that exits at once: its error text must be in the message, so the caller waits for the end of stderr
    // (not only for the process to exit, which can come first on a slow machine).
    [Fact]
    public async Task The_pump_finishes_only_after_every_line_was_handed_on()
    {
        var lines = new List<string>();
        var pump = StdioMcpSession.PumpStderr(new StringReader("bad config\r\nsecond"), l => { lock (lines) lines.Add(l); });
        await pump.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(["bad config", "second"], lines);
    }
}
