using Hotline.Core.Settings;

namespace Hotline.App;

public partial class App
{
    /// <summary>settings.json is the configuration: hand edits apply live (debounced; Hotline's own saves are no-ops).</summary>
    private void WatchSettingsFile(SettingsStore store)
    {
        var dir = Path.GetDirectoryName(store.FilePath)!;
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
        try
        {
            // settings.json, toolbar.json, icons\*.png and connections\*.json (one file per connection): edits, new and deleted files apply live.
            _settingsWatcher = new FileSystemWatcher(dir, "*") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, IncludeSubdirectories = true };
            var toolbarPath = Path.Combine(dir, "toolbar.json");
            var iconsDir = Path.Combine(dir, "icons");
            // icons\*.png too: a picture replaced under the same name changes no .json, but the bar must redraw (ApplyToolbarFile's IconStamp).
            bool Relevant(string? path) => path is not null && (string.Equals(path, store.FilePath, StringComparison.OrdinalIgnoreCase)
                || (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetDirectoryName(path), iconsDir, StringComparison.OrdinalIgnoreCase))
                || string.Equals(path, toolbarPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetDirectoryName(path), store.ConnectionsDirectory, StringComparison.OrdinalIgnoreCase));
            void Kick(string? path) { if (Relevant(path)) _popup.DispatcherQueue.TryEnqueue(() => { timer.Stop(); timer.Start(); }); }
            _settingsWatcher.Changed += (_, e) => Kick(e.FullPath);
            _settingsWatcher.Created += (_, e) => Kick(e.FullPath);
            _settingsWatcher.Deleted += (_, e) => Kick(e.FullPath);
            _settingsWatcher.Renamed += (_, e) => { Kick(e.FullPath); Kick(e.OldFullPath); };
            _settingsWatcher.Error += (_, e) =>
            {
                _log?.Error("settings watcher failed", e.GetException());
                _popup.DispatcherQueue.TryEnqueue(() => { timer.Stop(); timer.Start(); });
            };
            _settingsWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { _log?.Error("can't watch settings.json", ex); }
    }
}
