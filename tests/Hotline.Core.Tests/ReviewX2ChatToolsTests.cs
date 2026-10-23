using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

/// <summary>Fixes from the full-codebase review: chat file edge cases.</summary>
public sealed class ReviewX2ChatToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _clock = new();
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Prompt_library_treats_the_default_name_case_insensitively()
    {
        var lib = new PromptLibrary(_dir);
        lib.EnsureDefault();
        Assert.False(lib.Delete("Default")); // the same file on Windows
        Assert.True(File.Exists(lib.PathFor("default")));
        Assert.Equal(["default"], lib.List());
    }

    [Fact]
    public void Prune_skips_a_transcript_that_is_in_use()
    {
        var store = new HistoryStore(_dir, _clock);
        store.Append("busy", new ChatMessage("m", ChatRole.User, "x", [], _clock.GetUtcNow()));
        store.Append("old", new ChatMessage("m", ChatRole.User, "y", [], _clock.GetUtcNow()));
        foreach (var id in new[] { "busy", "old" }) File.SetLastWriteTimeUtc(store.PathFor(id), _clock.GetUtcNow().UtcDateTime.AddDays(-40));
        using (new FileStream(store.PathFor("busy"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(1, store.Prune(retentionDays: 30));
        Assert.True(File.Exists(store.PathFor("busy")));
        Assert.False(File.Exists(store.PathFor("old")));
    }

    [Fact]
    public void Memory_append_to_a_locked_file_returns_false_instead_of_throwing()
    {
        var path = Path.Combine(_dir, "memory.md");
        var store = new MemoryStore(path);
        store.EnsureFile();
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(store.Append("likes tea"));
        Assert.True(store.Append("likes tea"));
    }
}
