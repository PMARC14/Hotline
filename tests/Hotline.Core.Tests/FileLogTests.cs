using Hotline.Core.Diagnostics;

namespace Hotline.Core.Tests;

public sealed class FileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Writes_level_and_message_creating_directories()
    {
        var path = Path.Combine(_dir, "logs", "hotline.log");
        new FileLog(path).Info("key Tap via FastPath");
        Assert.Contains("INFO key Tap via FastPath", File.ReadAllText(path));
    }

    [Fact]
    public void Error_includes_exception()
    {
        var path = Path.Combine(_dir, "hotline.log");
        new FileLog(path).Error("boom", new InvalidOperationException("bad state"));
        var text = File.ReadAllText(path);
        Assert.Contains("ERROR boom", text);
        Assert.Contains("bad state", text);
    }

    [Fact]
    public void Rotates_when_over_max_size()
    {
        var path = Path.Combine(_dir, "hotline.log");
        var log = new FileLog(path, maxBytes: 200);
        for (var i = 0; i < 20; i++) log.Info($"line {i} padding padding padding");
        Assert.True(File.Exists(path + ".1"));
        Assert.True(new FileInfo(path).Length < 400);
    }

    [Fact]
    public void Never_throws_when_log_file_is_read_only()
    {
        var path = Path.Combine(_dir, "hotline.log");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "x");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var ex = Record.Exception(() => new FileLog(path).Info("still fine"));
            Assert.Null(ex);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Debug_lines_are_written_only_when_verbose()
    {
        var path = Path.Combine(_dir, "hotline.log");
        var log = new FileLog(path);
        log.Debug("hidden detail");
        log.Verbose = true;
        log.Debug("visible detail");
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("hidden detail", text);
        Assert.Contains("DEBUG visible detail", text);
    }
}
