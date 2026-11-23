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
        StdioMcpSession.PumpStderr(reader, l => { lock (lines) { lines.Add(l); if (l == "last") done.TrySetResult(); } });
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(["first", new string('x', 4000), "last"], lines);
    }
}
