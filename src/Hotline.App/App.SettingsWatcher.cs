using Hotline.Core.Settings;

namespace Hotline.App;

public partial class App
{
    private readonly List<FileSystemWatcher> _settingsWatchers = [];
    private bool _settingsWatchersDisposalHooked;

    /// <summary>settings.json is the configuration: hand edits apply live (debounced; Hotline's own saves are no-ops).</summary>
    private void WatchSettingsFile(SettingsStore store)
    {
        DisposeSettingsWatchers();

        var dir = Path.GetDirectoryName(store.FilePath)!;
        var toolbarPath = Path.Combine(dir, "toolbar.json");
        var mcpPath = Path.Combine(dir, "mcp.json");
        var iconsDir = Path.Combine(dir, "icons");

        var timer = _popup!.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(400);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            try
            {
                ApplyToolbarFile(dir);
                var fromDisk = store.TryRead();
                ReportSettingsProblems(store);
                if (fromDisk is null) { _log?.Info("settings.json not readable yet (being edited?); keeping current settings"); return; }
                if (_settingsService!.Reload(fromDisk)) _log?.Info("settings.json changed on disk; applied");
            }
            catch (Exception ex) { _log?.Error("reloading settings failed", ex); }
        };

        // settings.json, toolbar.json, mcp.json, icons\*.png and connections\*.json (one file per connection): edits, new and deleted files apply live.
        // icons\*.png too: a picture replaced under the same name changes no .json, but the bar must redraw (ApplyToolbarFile's IconStamp).
        bool Relevant(string? path) => path is not null && (string.Equals(path, store.FilePath, StringComparison.OrdinalIgnoreCase)
            || (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetDirectoryName(path), iconsDir, StringComparison.OrdinalIgnoreCase))
            || string.Equals(path, toolbarPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, mcpPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetDirectoryName(path), store.ConnectionsDirectory, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, store.ConnectionsDirectory, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, iconsDir, StringComparison.OrdinalIgnoreCase));

        void AttachWatcher(string folder)
        {
            lock (_settingsWatchers)
            {
                if (!Directory.Exists(folder)) return;
                var existing = _settingsWatchers.FirstOrDefault(w => string.Equals(w.Path, folder, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    try
                    {
                        if (existing.EnableRaisingEvents) return;
                        _settingsWatchers.Remove(existing);
                        existing.Dispose();
                    }
                    catch
                    {
                        _settingsWatchers.Remove(existing);
                    }
                }

                try
                {
                    var watcher = new FileSystemWatcher(folder, "*")
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                        IncludeSubdirectories = false
                    };
                    watcher.Changed += (_, e) => Kick(e.FullPath);
                    watcher.Created += (_, e) => Kick(e.FullPath);
                    watcher.Deleted += (_, e) => Kick(e.FullPath);
                    watcher.Renamed += (_, e) => { Kick(e.FullPath); Kick(e.OldFullPath); };
                    watcher.Error += (_, e) =>
                    {
                        _log?.Error($"settings watcher failed on {folder}", e.GetException());
                        _popup?.DispatcherQueue.TryEnqueue(() => { timer.Stop(); timer.Start(); });
                    };
                    watcher.EnableRaisingEvents = true;
                    _settingsWatchers.Add(watcher);
                    if (string.Equals(folder, dir, StringComparison.OrdinalIgnoreCase))
                        _settingsWatcher = watcher;
                }
                catch (Exception ex) when (ex is IOException or ArgumentException)
                {
                    _log?.Error($"can't watch {folder}", ex);
                }
            }
        }

        void Kick(string? path)
        {
            if (path is not null && (string.Equals(path, store.ConnectionsDirectory, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, iconsDir, StringComparison.OrdinalIgnoreCase)))
            {
                AttachWatcher(path);
            }
            if (Relevant(path)) _popup.DispatcherQueue.TryEnqueue(() => { timer.Stop(); timer.Start(); });
        }

        try { Directory.CreateDirectory(store.ConnectionsDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log?.Error("can't create connections folder", ex); }
        try { Directory.CreateDirectory(iconsDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log?.Error("can't create icons folder", ex); }

        AttachWatcher(dir);
        AttachWatcher(store.ConnectionsDirectory);
        AttachWatcher(iconsDir);

        if (!_settingsWatchersDisposalHooked)
        {
            _settingsWatchersDisposalHooked = true;
            _popup.Closed += (_, _) => DisposeSettingsWatchers();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeSettingsWatchers();
        }
    }

    private void DisposeSettingsWatchers()
    {
        lock (_settingsWatchers)
        {
            foreach (var watcher in _settingsWatchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch { }
            }
            _settingsWatchers.Clear();
            _settingsWatcher = null;
        }
    }
}
