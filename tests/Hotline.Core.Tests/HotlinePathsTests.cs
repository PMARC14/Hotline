using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class HotlinePathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [Fact]
    public void Data_directory_is_dot_hotline_in_profile()
        => Assert.Equal(Path.Combine(@"C:\Users\me", ".hotline"), HotlinePaths.DataDirectory(@"C:\Users\me"));

    [Fact]
    public void Migrates_settings_and_history_once()
    {
        var legacy = Path.Combine(_root, "legacy");
        var data = Path.Combine(_root, ".hotline");
        Directory.CreateDirectory(Path.Combine(legacy, "history"));
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{\"x\":1}");
        File.WriteAllText(Path.Combine(legacy, "history", "c1.jsonl"), "line");

        Assert.True(HotlinePaths.MigrateFromLegacy(legacy, data));
        Assert.Equal("{\"x\":1}", File.ReadAllText(Path.Combine(data, "settings.json")));
        Assert.Equal("line", File.ReadAllText(Path.Combine(data, "history", "c1.jsonl")));

        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{\"x\":2}");
        Assert.False(HotlinePaths.MigrateFromLegacy(legacy, data)); // new location wins from now on
        Assert.Equal("{\"x\":1}", File.ReadAllText(Path.Combine(data, "settings.json")));
    }

    [Fact]
    public void Nothing_to_migrate_is_fine()
        => Assert.False(HotlinePaths.MigrateFromLegacy(Path.Combine(_root, "none"), Path.Combine(_root, ".hotline")));
}
