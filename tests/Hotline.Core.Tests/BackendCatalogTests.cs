using Hotline.Core.Backends;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class BackendCatalogTests : TempDirTest
{
    [Theory]
    [InlineData("claude", "CLAUDE")]
    [InlineData("CLAUDE", "claude")]
    public async Task Cache_lookup_and_invalidation_ignore_connection_id_case(string lookup, string invalidate)
    {
        var created = 0;
        var cache = new BackendCache(() => [new ConnectionProfile { Id = "Claude" }], _ => { created++; return new FakeBackend(); });
        var first = cache.Get(lookup);
        Assert.NotNull(first);
        Assert.Same(first, cache.Get("Claude"));
        Assert.Equal(1, created);
        await cache.InvalidateAsync(invalidate);
        Assert.NotSame(first, cache.Get(lookup));
        Assert.Equal(2, created);
        await cache.DisposeAllAsync();
    }

    private BackendDeps Deps() => new(new FakeLineProcessFactory(), new AgyWorkspace(_dir, new ManualTimeProvider()),
        new FileLog(Path.Combine(_dir, "h.log")), _ => false, null, null, _ => "prompt", _dir);

    [Fact]
    public void Agy_profile_creates_agy_backend()
    {
        var b = BackendFactory.Create(new ConnectionProfile { Id = "agy", Type = BackendType.Antigravity, Name = "Gemini (Antigravity)" }, Deps());
        Assert.IsType<AgyBackend>(b);
        Assert.Equal("Gemini (Antigravity)", b!.DisplayName);
    }

    [Theory]
    [InlineData(BackendType.Gemini, typeof(Hotline.Core.Backends.Api.GeminiBackend))]
    [InlineData(BackendType.OpenAiCompatible, typeof(Hotline.Core.Backends.Api.OpenAiBackend))]
    [InlineData(BackendType.Local, typeof(Hotline.Core.Backends.Api.OpenAiBackend))]
    [InlineData(BackendType.Anthropic, typeof(Hotline.Core.Backends.Api.AnthropicBackend))]
    [InlineData(BackendType.ClaudeCode, typeof(Hotline.Core.Backends.ClaudeCode.ClaudeCodeBackend))]
    public void Api_and_cli_backends_are_available(BackendType type, Type expected)
    {
        Assert.IsType(expected, BackendFactory.Create(new ConnectionProfile { Id = "x", Type = type }, Deps()));
        Assert.True(BackendFactory.IsAvailable(type));
    }

    [Fact]
    public async Task Cache_creates_once_per_id_and_disposes_all()
    {
        var created = 0;
        var profiles = new List<ConnectionProfile> { new() { Id = "a", Type = BackendType.Antigravity } };
        var fake = new FakeBackend();
        var cache = new BackendCache(() => profiles, _ => { created++; return fake; });

        Assert.Same(fake, cache.Get("a"));
        Assert.Same(fake, cache.Get("a"));
        Assert.Null(cache.Get("missing"));
        Assert.Equal(1, created);
        await cache.DisposeAllAsync();
    }

    [Fact]
    public async Task Invalidate_disposes_and_recreates_on_next_get()
    {
        var created = 0;
        var profiles = new List<ConnectionProfile> { new() { Id = "a", Type = BackendType.Antigravity } };
        var cache = new BackendCache(() => profiles, _ => { created++; return new FakeBackend(); });
        cache.Get("a");
        await cache.InvalidateAsync("a");
        cache.Get("a");
        Assert.Equal(2, created);
        await cache.InvalidateAsync("missing"); // no-op
    }
}
