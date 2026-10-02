using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Processes;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

public sealed record BackendDeps(
    ILineProcessFactory Processes, AgyWorkspace AgyWorkspace, FileLog Log,
    Func<string, bool> FileExists, string? LocalAppData, string? PathEnv,
    Func<BackendProfile, string> SystemPrompt, string HomeDirectory);

public static class BackendFactory
{
    public static bool IsAvailable(BackendType type) => type == BackendType.Antigravity;

    /// <summary>Creates the backend for a profile, or null if that backend type isn't implemented yet (Plan 3).</summary>
    public static IChatBackend? Create(BackendProfile p, BackendDeps deps) => p.Type switch
    {
        BackendType.Antigravity => new AgyBackend(p,
            () => AgyLocator.Find(p.CliPath, deps.FileExists, deps.LocalAppData, deps.PathEnv),
            deps.AgyWorkspace, deps.Processes, deps.Log, deps.SystemPrompt, deps.HomeDirectory),
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
