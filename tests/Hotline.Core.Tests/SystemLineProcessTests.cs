using Hotline.Core.Processes;

namespace Hotline.Core.Tests;

public class SystemLineProcessTests
{
    [Fact]
    public async Task Writes_utf8_without_bom_and_reads_lines()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var p = new SystemLineProcessFactory().Start("powershell.exe",
            ["-NoProfile", "-Command", "$l = [Console]::In.ReadLine(); [Console]::Out.WriteLine('got:' + $l + ':' + $l.Length)"],
            Path.GetTempPath());
        await p.WriteLineAsync("hello", default);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Assert.Equal("got:hello:5", await p.ReadLineAsync(cts.Token)); // a BOM would make the length 6+
    }
}
