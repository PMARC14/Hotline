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

    /// <summary>
    /// Takes settings re-read from disk (the user edited settings.json) into the live objects other components hold.
    /// Returns false (and raises nothing) when nothing changed, e.g. for Hotline's own saves.
    /// </summary>
    public bool Reload(HotlineSettings fromDisk)
    {
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        if (System.Text.Json.JsonSerializer.Serialize(fromDisk, options) == System.Text.Json.JsonSerializer.Serialize(Current, options)) return false;
        CopyInto(fromDisk.Activation, Current.Activation);
        CopyInto(fromDisk.Diagnostics, Current.Diagnostics);
        CopyInto(fromDisk.Window, Current.Window);
        CopyInto(fromDisk.Chat, Current.Chat);
        Current.SchemaVersion = fromDisk.SchemaVersion;
        Changed?.Invoke();
        return true;
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

    public void Update(Action<HotlineSettings> change)
    {
        change(Current);
        SettingsStore.Normalize(Current);
        try { store.Save(Current); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Error("saving settings failed", ex); }
        Changed?.Invoke();
    }
}
