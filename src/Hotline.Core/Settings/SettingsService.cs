using Hotline.Core.Diagnostics;

namespace Hotline.Core.Settings;

/// <summary>
/// The single way to change settings at runtime: mutate, normalize, save, notify. Components keep references to
/// the same settings objects, so most changes apply live; Changed lets the UI re-apply appearance.
/// </summary>
public sealed class SettingsService(SettingsStore store, HotlineSettings settings, FileLog log)
{
    public HotlineSettings Current { get; } = settings;

    public event Action? Changed;

    public void Update(Action<HotlineSettings> change)
    {
        change(Current);
        SettingsStore.Normalize(Current);
        try { store.Save(Current); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Error("saving settings failed", ex); }
        Changed?.Invoke();
    }
}
