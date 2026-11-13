using System.Text;

namespace Hotline.Core.Tests;

public sealed class DurableFileTests : TempDirTest
{
    [Fact]
    public void Publication_failure_cleans_temp_and_preserves_destination_directory()
    {
        var path = Path.Combine(_dir, "blocked");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "original"), "kept");
        var failure = Record.Exception(() => DurableFile.WriteText(path, "new"));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Empty(Directory.GetFiles(_dir));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(path, "original")));
    }
    [Fact]
    public void Flush_failure_preserves_destination_and_cleans_unique_temp()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "original");
        File.WriteAllText(path + ".tmp", "unrelated");
        string? temp = null;
        Assert.Throws<IOException>(() => DurableFile.WriteBytes(path, Encoding.UTF8.GetBytes("new"), stream =>
        {
            temp = stream.Name;
            Assert.NotEqual(path + ".tmp", temp);
            Assert.Equal("original", File.ReadAllText(path));
            throw new IOException("flush failed");
        }));
        Assert.NotNull(temp);
        Assert.False(File.Exists(temp));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal("unrelated", File.ReadAllText(path + ".tmp"));
    }

    [Fact]
    public void Publishes_utf8_without_bom_after_flush()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "config.json");
        var flushed = false;
        DurableFile.WriteBytes(path, Encoding.UTF8.GetBytes("é"), stream =>
        {
            Assert.False(File.Exists(path));
            stream.Flush(true);
            flushed = true;
        });
        Assert.True(flushed);
        Assert.Equal(Encoding.UTF8.GetBytes("é"), File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(_dir));
    }
}
