using Hotline.Core.Diagnostics;

namespace Hotline.Core.Tests;

public sealed class FileLogTests : TempDirTest
{

    [Fact]
    public void Writes_level_and_message_creating_directories_and_errors_include_the_exception()
    {
        var path = Path.Combine(_dir, "logs", "hotline.log");
        var log = new FileLog(path);
        log.Info("key Tap via FastPath");
        log.Error("boom", new InvalidOperationException("bad state"));
        var text = File.ReadAllText(path);
        Assert.Contains("INFO key Tap via FastPath", text);
        Assert.Contains("ERROR boom", text);
        Assert.Contains("bad state", text);
    }

    [Fact]
    public void ErrorCount_counts_the_errors_written_even_when_the_file_cannot_be_written()
    {
        var log = new FileLog(Path.Combine(_dir, "hotline.log"));
        log.Info("fine");
        log.Error("one");
        log.Error("two", new InvalidOperationException());
        Assert.Equal(2, log.ErrorCount);
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
