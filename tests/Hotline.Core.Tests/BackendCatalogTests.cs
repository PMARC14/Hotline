using Hotline.Core.Backends;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class BackendCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private BackendDeps Deps() => new(new FakeLineProcessFactory(), new AgyWorkspace(_dir, new ManualTimeProvider()),
        new FileLog(Path.Combine(_dir, "h.log")), _ => false, null, null, _ => "prompt", _dir);

    [Fact]
    public void Agy_profile_creates_agy_backend()
    {
        var b = BackendFactory.Create(new BackendProfile { Id = "agy", Type = BackendType.Antigravity, Name = "Gemini (Antigravity)" }, Deps());
        Assert.IsType<AgyBackend>(b);
        Assert.Equal("Gemini (Antigravity)", b!.DisplayName);
    }

    [Theory]
    [InlineData(BackendType.Gemini)]
    [InlineData(BackendType.OpenAiCompatible)]
    public void Api_backends_are_not_available_yet(BackendType type)
    {
        Assert.Null(BackendFactory.Create(new BackendProfile { Id = "x", Type = type }, Deps()));
        Assert.False(BackendFactory.IsAvailable(type));
    }

    [Fact]
    public async Task Cache_creates_once_per_id_and_disposes_all()
    {
        var created = 0;
        var profiles = new List<BackendProfile> { new() { Id = "a", Type = BackendType.Antigravity } };
        var fake = new FakeBackend();
        var cache = new BackendCache(() => profiles, _ => { created++; return fake; });

        Assert.Same(fake, cache.Get("a"));
        Assert.Same(fake, cache.Get("a"));
        Assert.Null(cache.Get("missing"));
        Assert.Equal(1, created);
        await cache.DisposeAllAsync();
        Assert.Null(cache.Get("missing"));
    }

    [Fact]
    public async Task Invalidate_disposes_and_recreates_on_next_get()
    {
        var created = 0;
        var profiles = new List<BackendProfile> { new() { Id = "a", Type = BackendType.Antigravity } };
        var cache = new BackendCache(() => profiles, _ => { created++; return new FakeBackend(); });
        cache.Get("a");
        await cache.InvalidateAsync("a");
        cache.Get("a");
        Assert.Equal(2, created);
        await cache.InvalidateAsync("missing"); // no-op
    }

    [Fact]
    public void Claude_code_is_available()
    {
        Assert.True(BackendFactory.IsAvailable(BackendType.ClaudeCode));
        Assert.IsType<Hotline.Core.Backends.ClaudeCode.ClaudeCodeBackend>(BackendFactory.Create(new BackendProfile { Id = "cc", Type = BackendType.ClaudeCode }, Deps()));
    }
}
