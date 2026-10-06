using System.Diagnostics;
using System.Text;
using Hotline.Core.Tools;

namespace Hotline.Core.Tests;

public class McpProcessTests
{
    private static McpServerConfig Server(string command, string[] args, Dictionary<string, string>? env = null, string? cwd = null) =>
        new("fs", command, args, env ?? new Dictionary<string, string>(), WorkingDirectory: cwd);

    [Fact]
    public void Wraps_the_command_in_cmd_on_windows_like_the_sdk()
    {
        var psi = McpProcess.CreateStartInfo(Server("npx", ["-y", "@modelcontextprotocol/server-filesystem", "C:\\work"]), windows: true);
        Assert.Equal("cmd.exe", psi.FileName);
        Assert.Equal(["/c", "npx", "-y", "@modelcontextprotocol/server-filesystem", "C:\\work"], psi.ArgumentList);
    }

    [Fact]
    public void Does_not_wrap_cmd_itself_or_on_other_platforms()
    {
        var cmd = McpProcess.CreateStartInfo(Server(@"C:\Windows\System32\CMD.EXE", ["/c", "x"]), windows: true);
        Assert.Equal(@"C:\Windows\System32\CMD.EXE", cmd.FileName);
        Assert.Equal(["/c", "x"], cmd.ArgumentList);
        var unix = McpProcess.CreateStartInfo(Server("npx", ["a&b"]), windows: false);
        Assert.Equal("npx", unix.FileName);
        Assert.Equal(["a&b"], unix.ArgumentList);
    }

    [Fact]
    public void Caret_escapes_shell_characters_only_in_arguments_without_whitespace()
    {
        var psi = McpProcess.CreateStartInfo(Server("uvx", ["a&b|c<d>e^f", "has space & amp"]), windows: true);
        Assert.Equal(["/c", "uvx", "a^&b^|c^<d^>e^^f", "has space & amp"], psi.ArgumentList);
    }

    [Fact]
    public void Streams_are_redirected_utf8_without_bom_and_no_window()
    {
        var psi = McpProcess.CreateStartInfo(Server("node", []), windows: true);
        Assert.True(psi.RedirectStandardInput && psi.RedirectStandardOutput && psi.RedirectStandardError);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.CreateNoWindow);
        foreach (var e in new[] { psi.StandardInputEncoding, psi.StandardOutputEncoding, psi.StandardErrorEncoding })
        {
            Assert.IsType<UTF8Encoding>(e);
            Assert.Empty(e!.GetPreamble());
        }
    }

    [Fact]
    public void Working_directory_defaults_to_the_current_directory()
    {
        Assert.Equal(Environment.CurrentDirectory, McpProcess.CreateStartInfo(Server("node", []), windows: true).WorkingDirectory);
        Assert.Equal(@"D:\x", McpProcess.CreateStartInfo(Server("node", [], cwd: @"D:\x"), windows: true).WorkingDirectory);
    }

    [Fact]
    public void Environment_is_inherited_with_the_server_overrides_on_top()
    {
        var psi = McpProcess.CreateStartInfo(Server("node", [], new() { ["HOTLINE_TEST_VAR"] = "1", ["PATH"] = "custom" }), windows: true);
        Assert.Equal("1", psi.Environment["HOTLINE_TEST_VAR"]);
        Assert.Equal("custom", psi.Environment["PATH"]);
        Assert.Equal(Environment.GetEnvironmentVariable("SystemRoot"), psi.Environment["SystemRoot"]);
    }

    [Fact]
    public void Stderr_throttle_passes_a_burst_then_reports_what_it_dropped()
    {
        var clock = new ManualTimeProvider();
        var lines = new List<string>();
        var throttle = new StderrThrottle("fs", lines.Add, clock, perMinute: 3);
        for (var i = 0; i < 10; i++) throttle.Line($"l{i}");
        Assert.Equal(["fs: l0", "fs: l1", "fs: l2"], lines);
        clock.Advance(TimeSpan.FromMinutes(1));
        throttle.Line("later");
        Assert.Equal(["fs: l0", "fs: l1", "fs: l2", "fs: (7 stderr lines skipped)", "fs: later"], lines);
    }

    [Fact]
    public void Stderr_throttle_keeps_a_short_tail_for_error_messages()
    {
        var throttle = new StderrThrottle("fs", _ => { }, new ManualTimeProvider(), perMinute: 1, tailLines: 2);
        foreach (var l in new[] { "a", "b", "c" }) throttle.Line(l);
        Assert.Equal("b\nc", throttle.Tail);
    }

    [Fact]
    public async Task A_server_that_exits_at_once_fails_fast_with_its_exit_code_and_stderr()
    {
        if (!OperatingSystem.IsWindows()) return;
        var started = new List<int>();
        var connect = StdioMcpSession.Connector(p => started.Add(p.Id), _ => { });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => connect(Server("cmd.exe", ["/c", "echo bad config 1>&2 & exit 3"]), cts.Token));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"took {sw.Elapsed}");
        Assert.Contains("exit code 3", ex.Message);
        Assert.Contains("bad config", ex.Message);
        Assert.Single(started);
    }

    [Fact]
    public async Task A_command_that_isnt_installed_fails_with_a_clear_message_without_starting_anything()
    {
        if (!OperatingSystem.IsWindows()) return;
        var started = new List<int>();
        var connect = StdioMcpSession.Connector(p => started.Add(p.Id), _ => { });
        var ex = await Assert.ThrowsAsync<IOException>(() => connect(Server("hotline-no-such-tool", ["--x"]), CancellationToken.None));
        Assert.Contains("isn't installed", ex.Message);
        Assert.Empty(started);
    }

    [Fact]
    public async Task A_failed_connect_kills_the_server_and_its_children()
    {
        if (!OperatingSystem.IsWindows()) return;
        var started = new List<int>();
        var connect = StdioMcpSession.Connector(p => started.Add(p.Id), _ => { });
        var pidFile = Path.Combine(Path.GetTempPath(), $"hotline-mcp-{Guid.NewGuid():N}.pid");
        using var cts = new CancellationTokenSource();
        // cmd → powershell that never answers; cancellation must take the whole tree down.
        var connecting = connect(
            Server("cmd.exe", ["/c", $"powershell.exe -NoProfile -Command Set-Content -Path '{pidFile}' -Value $PID; Start-Sleep 120"]), cts.Token);
        // Cancel only once the grandchild exists (PowerShell can take many seconds to start on CI runners).
        var childPid = await ReadPidAsync(pidFile, TimeSpan.FromSeconds(60));
        var sw = Stopwatch.StartNew();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<Exception>(() => connecting);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"cancelled connect took {sw.Elapsed} to tear down");
        var rootPid = Assert.Single(started);
        File.Delete(pidFile);
        await Task.Delay(500);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(rootPid));
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(childPid));
    }

    /// <summary>Waits for a process to write its PID (the file can exist before its content is complete).</summary>
    private static async Task<int> ReadPidAsync(string path, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var pid)) return pid;
            }
            catch (IOException) { } // still being written
            await Task.Delay(100);
        }
        throw new TimeoutException($"no PID in {path} after {timeout}");
    }
}
