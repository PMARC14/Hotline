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
    /// <summary>After a reload from disk: ids of connections whose settings changed or that were removed.</summary>
    public event Action<IReadOnlyList<string>>? ConnectionsChanged;

    /// <summary>
    /// Takes settings re-read from disk (the user edited settings.json) into the live objects other components hold.
    /// Returns false (and raises nothing) when nothing changed, e.g. for Hotline's own saves.
    /// </summary>
    public bool Reload(HotlineSettings fromDisk)
    {
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        if (File.Exists(store.FilePath)) _knownWriteUtc = File.GetLastWriteTimeUtc(store.FilePath);
        if (System.Text.Json.JsonSerializer.Serialize(fromDisk, options) == System.Text.Json.JsonSerializer.Serialize(Current, options)) return false;
        var before = Current.Chat.Backends.ToDictionary(b => b.Id, b => System.Text.Json.JsonSerializer.Serialize(b, options));
        CopyInto(fromDisk.Activation, Current.Activation);
        CopyInto(fromDisk.Diagnostics, Current.Diagnostics);
        CopyInto(fromDisk.Window, Current.Window);
        MergeChat(fromDisk.Chat, Current.Chat);
        Current.SchemaVersion = fromDisk.SchemaVersion;
        var changed = before.Where(kv => Current.Chat.Backends.FirstOrDefault(b => b.Id == kv.Key) is not { } now
                                         || System.Text.Json.JsonSerializer.Serialize(now, options) != kv.Value)
            .Select(kv => kv.Key).ToList();
        Changed?.Invoke();
        if (changed.Count > 0) ConnectionsChanged?.Invoke(changed);
        return true;
    }

    /// <summary>
    /// Chat settings: connections are matched by id and updated in place, so backends, the bottom bar and an open
    /// settings window keep pointing at the live objects. New ones are added, missing ones removed, disk order kept.
    /// </summary>
    private static void MergeChat(ChatSettings from, ChatSettings to)
    {
        var existing = to.Backends.ToDictionary(b => b.Id);
        var merged = new List<BackendProfile>();
        foreach (var profile in from.Backends)
        {
            if (existing.TryGetValue(profile.Id, out var live)) { CopyInto(profile, live); merged.Add(live); }
            else merged.Add(profile);
        }
        var backends = to.Backends;
        CopyInto(from, to);
        to.Backends = backends; // keep the list instance
        backends.Clear();
        backends.AddRange(merged);
    }

    /// <summary>Copies every property, keeping the target instance (and the same list instance for lists).</summary>
    private static void CopyInto<T>(T from, T to) where T : class
    {
        foreach (var prop in typeof(T).GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            var value = prop.GetValue(from);
            if (value is System.Collections.IList source && prop.GetValue(to) is System.Collections.IList target)
            {
                target.Clear();
                foreach (var item in source) target.Add(item);
            }
            else prop.SetValue(to, value);
        }
    }

    private DateTime _knownWriteUtc = DateTime.MinValue;

    public void Update(Action<HotlineSettings> change)
    {
        // A hand edit saved moments ago may not have been reloaded yet (the file watcher debounces): take it in first
        // so this save doesn't overwrite it.
        if (File.Exists(store.FilePath) && File.GetLastWriteTimeUtc(store.FilePath) > _knownWriteUtc && store.TryRead() is { } fromDisk)
            Reload(fromDisk);
        change(Current);
        SettingsStore.Normalize(Current);
        try
        {
            // The file on disk may be a half-finished hand edit (not valid JSON): keep a copy before replacing it.
            if (File.Exists(store.FilePath) && store.TryRead() is null)
            {
                var backup = $"{store.FilePath}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                File.Copy(store.FilePath, backup, overwrite: true);
                log.Info($"settings.json wasn't valid; kept it as {backup} before saving");
            }
            store.Save(Current);
            _knownWriteUtc = File.GetLastWriteTimeUtc(store.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Error("saving settings failed", ex); }
        Changed?.Invoke();
    }
}
