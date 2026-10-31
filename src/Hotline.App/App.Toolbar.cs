namespace Hotline.App;

public partial class App
{
    /// <summary>
    /// ~/.hotline/toolbar.json → the button rows (written with the defaults on first run; applies
    /// live). A file that can't be read keeps the current bar (the defaults at start); one that reads the same as the bar
    /// in force changes nothing. Resolve already gives the defaults for an empty or all-invalid file, so a half-written
    /// file never blanks the bar.
    /// </summary>
    private void ApplyToolbarFile(string dataDir)
    {
        if (_popup is null) return;
        var load = Hotline.Core.Windowing.ToolbarFile.Load(Path.Combine(dataDir, "toolbar.json"));
        ReportToolbarProblems(load);
        if (load.Error is { } error)
        {
            _log?.Error(error);
            ThisPc.Toolbar = (_popup.ToolbarModel, error);
            if (_toolbarApplied) return; // keep the current bar
        }
        else ThisPc.Toolbar = (load.Model, null);
        // Logged whenever they change, also when the layout doesn't (an edit adding only invalid entries); the file is left as it is.
        if (load.Error is null && !load.Model.Warnings.SequenceEqual(_toolbarWarnings))
        {
            _toolbarWarnings = load.Model.Warnings;
            foreach (var warning in _toolbarWarnings) _log?.Info($"toolbar.json: {warning}");
        }
        // A picture replaced under the same name leaves the model equal: the icons folder's files decide too.
        var iconStamp = IconStamp(dataDir);
        if (_toolbarApplied && load.Model.Equals(_popup.ToolbarModel) && iconStamp == _iconStamp) return;
        _iconStamp = iconStamp;
        if (!Enum.GetValues<Hotline.Core.Windowing.Quadrant>().SelectMany(load.Model.Items).Concat(load.Model.Menu).Contains("settings"))
            _log?.Info("toolbar.json places no Settings button: Settings opens from the tray icon only");
        _toolbarApplied = true;
        try { _popup.ApplyToolbar(load.Model); }
        catch (Exception ex) { _log?.Error("applying toolbar.json failed", ex); }
    }

    /// <summary>
    /// The width (DIP) the button rows are planned at: the NARROWEST width the bar is shown at, so nothing planned to fit is cut off at
    /// the other. That is the smaller of the width the panel opens with (the width setting, or its percentage of the work area of the
    /// monitor it would open on, see PopupWindow.OpeningWidthDip) and the live panel's width when it has been laid out, less the panel's side
    /// padding. Settings' "Move what doesn't fit into the menu" plans with it. Without a panel (or if the monitor can't be asked) the
    /// width setting, else the default room of 11 buttons.
    /// </summary>
    private double BarRoom(Hotline.Core.Settings.WindowSettings window)
    {
        var padding = 2 * Hotline.Core.Windowing.ToolbarPlan.PanelPadding; // the panel's side padding (PopupWindow.xaml)
        try
        {
            var width = _popup?.OpeningWidthDip() ?? window.Width ?? Hotline.Core.Windowing.ToolbarPlan.WidthForButtons(11);
            if (_popup is { } popup && popup.Root.ActualWidth is > 0 and var live) width = Math.Min(width, live);
            return Math.Max(0, width - padding);
        }
        catch (Exception ex)
        {
            _log?.Error("working out the panel width for the button rows failed", ex);
            return Math.Max(0, (window.Width ?? Hotline.Core.Windowing.ToolbarPlan.WidthForButtons(11)) - padding);
        }
    }

    private bool _toolbarApplied;
    private IReadOnlyList<string> _toolbarWarnings = [];
    private string _toolbarNoticeKey = "";
    private string _iconStamp = "";

    /// <summary>The custom button pictures as name, size and time of each file: changes when one is added, replaced or removed.</summary>
    private static string IconStamp(string dataDir)
    {
        try
        {
            var folder = Path.Combine(dataDir, "icons");
            return Directory.Exists(folder)
                ? string.Join("|", new DirectoryInfo(folder).EnumerateFiles("*.png").OrderBy(f => f.Name, StringComparer.Ordinal)
                    .Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc.Ticks}"))
                : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>
    /// A file that can't be read, or parts of it that were ignored, show as a notice in the panel (with a way to Settings,
    /// where they are listed); it's raised again only when the problems change, and taken away when the file is fine.
    /// </summary>
    private void ReportToolbarProblems(Hotline.Core.Windowing.ToolbarLoad load)
    {
        const string tag = "toolbar-problems";
        var key = Hotline.Core.Diagnostics.ConfigProblems.Key(load.Error, load.Model.Warnings);
        if (key == _toolbarNoticeKey || _presenter?.Notices is not { } notices) return;
        _toolbarNoticeKey = key;
        var openSettings = (Strings.Bar_OpenSettings, new Action(() => _settingsHost?.Show()));
        if (key.Length == 0) notices.Remove(tag);
        else if (load.Error is { } error)
            notices.ShowTagged(tag, Strings.Bar_NoticeUnreadableTitle, $"{error} {Strings.Bar_NoticeKept}",
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error, closable: true, action: openSettings);
        else
            notices.ShowTagged(tag, Strings.Bar_NoticeProblemsTitle,
                L.F(Strings.Bar_NoticeProblems, Hotline.Core.Diagnostics.ConfigProblems.Summary(load.Model.Warnings)),
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning, closable: true, action: openSettings);
    }
}
