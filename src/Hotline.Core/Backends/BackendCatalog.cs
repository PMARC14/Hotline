using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Processes;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

public sealed record BackendDeps(
    ILineProcessFactory Processes, AgyWorkspace AgyWorkspace, FileLog Log,
    Func<string, bool> FileExists, string? LocalAppData, string? PathEnv,
    Func<BackendProfile, string> SystemPrompt, string HomeDirectory, string? ClaudeWorkspace = null,
    ISecretStore? Secrets = null, HttpClient? Http = null);

public static class BackendFactory
{
    public static bool IsAvailable(BackendType type) => Enum.IsDefined(type);

    /// <summary>Creates the backend for a profile, or null if that backend type isn't implemented yet (Plan 3).</summary>
    private static readonly Lazy<HttpClient> SharedHttp = new(CreateApiHttpClient);

    /// <summary>
    /// For key-carrying API calls: never follows redirects (a redirect could carry the key header to another host),
    /// and no overall time limit (long, thinking-heavy answers stream for many minutes; Stop cancels).
    /// </summary>
    public static HttpClient CreateApiHttpClient() =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(30) }) { Timeout = Timeout.InfiniteTimeSpan };
    private static HttpClient Http(BackendDeps deps) => deps.Http ?? SharedHttp.Value;
    private static ISecretStore Secrets(BackendDeps deps) => deps.Secrets ?? new InMemorySecretStore();

    public static IChatBackend? Create(BackendProfile p, BackendDeps deps) => p.Type switch
    {
        BackendType.Antigravity => new AgyBackend(p,
            () => AgyLocator.Find(p.CliPath, deps.FileExists, deps.LocalAppData, deps.PathEnv),
            deps.AgyWorkspace, deps.Processes, deps.Log, deps.SystemPrompt, deps.HomeDirectory),
        BackendType.ClaudeCode => new ClaudeCode.ClaudeCodeBackend(p,
            () => ClaudeCode.ClaudeLocator.Find(p.CliPath, deps.FileExists, deps.HomeDirectory, deps.PathEnv),
            deps.ClaudeWorkspace ?? Path.Combine(Path.GetTempPath(), "hotline-claude"), deps.Processes, deps.Log, deps.SystemPrompt, deps.HomeDirectory),
        BackendType.OpenAiCompatible or BackendType.Local => new Api.OpenAiBackend(p, Http(deps), Secrets(deps), deps.SystemPrompt, deps.Log),
        BackendType.Anthropic => new Api.AnthropicBackend(p, Http(deps), Secrets(deps), deps.SystemPrompt, deps.Log),
        BackendType.Gemini => new Api.GeminiBackend(p, Http(deps), Secrets(deps), deps.SystemPrompt, deps.Log),
        _ => null,
    };
}

/// <summary>One live backend instance per profile id (backends like agy hold a running process).</summary>
public sealed class BackendCache(Func<IReadOnlyList<BackendProfile>> profiles, Func<BackendProfile, IChatBackend?> create)
{
    private readonly Dictionary<string, IChatBackend?> _instances = [];

    public IChatBackend? Get(string id)
    {
        if (_instances.TryGetValue(id, out var existing)) return existing;
        var profile = profiles().FirstOrDefault(p => p.Id == id);
        if (profile is null) return null;
        return _instances[id] = create(profile);
    }

    /// <summary>Drops the live instance for a connection (after its settings change); the next Get recreates it.</summary>
    public async ValueTask InvalidateAsync(string id)
    {
        if (!_instances.Remove(id, out var backend) || backend is null) return;
        await backend.DisposeAsync();
    }

    public async ValueTask DisposeAllAsync()
    {
        foreach (var b in _instances.Values)
            if (b is not null) await b.DisposeAsync();
        _instances.Clear();
    }
}
