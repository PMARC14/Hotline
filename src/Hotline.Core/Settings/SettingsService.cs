using Hotline.Core.Diagnostics;

namespace Hotline.Core.Settings;

/// <summary>
/// The single way to change settings at runtime: mutate, normalize, save, notify. Components keep references to
/// the same settings objects, so most changes apply live; Changed lets the UI re-apply appearance.
/// </summary>
public sealed class SettingsService(SettingsStore store, HotlineSettings settings, FileLog log)
{
    public HotlineSettings Current { get; } = settings;

    /// <summary>What reading settings.json and the connection files ignored (see <see cref="SettingsStore.SettingsProblems"/>).</summary>
    public IReadOnlyList<string> Problems => store.SettingsProblems;

    /// <summary>Set while settings.json as a whole can't be used; the settings in force are then kept.</summary>
    public string? FileError => store.SettingsError;

    public event Action? Changed;
    /// <summary>After a save that left something undone (a file that couldn't be backed up or written, two connections for one file): the problems.</summary>
    public event Action<IReadOnlyList<string>>? SaveFailed;
    /// <summary>After a reload from disk: ids of connections whose settings changed or that were removed.</summary>
    public event Action<IReadOnlyList<string>>? ConnectionsChanged;

    /// <summary>
    /// Settings › Advanced › Reset (see <see cref="SettingsReset"/>), applied live. Never throws for a file problem: what could
    /// not be backed up, deleted or written is listed in <see cref="ResetResult.Failed"/>.
    /// </summary>
    public ResetResult Reset(bool everything)
    {
        ResetResult result;
        try { result = SettingsReset.Reset(store, Path.GetDirectoryName(store.FilePath)!, Current, everything, DateTimeOffset.Now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error("settings reset failed", ex);
            result = new ResetResult("", [], [ex.GetType().Name]);
        }
        Reload(store.TryRead() ?? store.Load()); // Load when a scanner held the file a moment
        log.Info($"settings reset ({(everything ? "everything" : "settings")}); backup in {result.Backup}"
                 + (result.Failed.Count > 0 ? $"; {result.Failed.Count} file(s) could not be handled" : ""));
        return result;
    }

    /// <summary>
    /// Takes settings re-read from disk (the user edited settings.json) into the live objects other components hold.
    /// Returns false (and raises nothing) when nothing changed, e.g. for Hotline's own saves. Always called on the UI thread
    /// (the file watcher's timer, <see cref="Update"/> from Settings, <see cref="Reset"/>), which is why the live connection
    /// list is changed in place.
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
        CopyInto(fromDisk.General, Current.General);
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
    private bool _unsaved; // the last save failed: memory is newer than the disk, so a re-read must not replace it

    /// <summary>
    /// Applies the change in memory and saves it. Returns what the save could not do (empty when all went well); the same list is
    /// raised as <see cref="SaveFailed"/>. The change itself always applies in memory.
    /// </summary>
    public IReadOnlyList<string> Update(Action<HotlineSettings> change)
    {
        // A hand edit saved moments ago (settings.json or a connection file) may not have been reloaded yet (the file watcher
        // debounces): take a fresh snapshot in first, so this save doesn't overwrite it or delete a file that was just added.
        var fromDisk = store.TryRead(); // once: it re-reads every connection file
        if (fromDisk is not null && !_unsaved) Reload(fromDisk);
        var before = Current.Chat.Backends.Select(b => b.Id).ToList();
        change(Current);
        SettingsStore.Normalize(Current);
        // Only a connection the change removed loses its file; one that merely isn't in memory yet is never deleted.
        var kept = Current.Chat.Backends.Select(b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = before.Where(id => !kept.Contains(id)).ToList();
        IReadOnlyList<string> problems = [];
        try
        {
            // A file that isn't what this process wrote (a hand edit, half-finished or with ignored values) is copied to
            // <file>.<time>.bak by the store before it is replaced; without that copy it is left alone and a problem is listed.
            store.Save(Current, removed);
            _unsaved = store.WriteProblems.Count > 0; // something was skipped: memory is ahead of the disk
            if (File.Exists(store.FilePath)) _knownWriteUtc = File.GetLastWriteTimeUtc(store.FilePath);
            problems = [.. store.WriteProblems];
            foreach (var problem in problems) log.Info($"settings: {problem}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _unsaved = true;
            log.Error("saving settings failed", ex);
            problems = [ex.GetType().Name];
        }
        Changed?.Invoke();
        if (problems.Count > 0) SaveFailed?.Invoke(problems);
        return problems;
    }
}
