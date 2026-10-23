using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;

namespace Hotline.App;

public partial class App
{
    private string _settingsNoticeKey = "";

    /// <summary>
    /// Ignored values in settings.json or a connection file, or a settings.json that can't be read at all, show as a notice in
    /// the panel (with a way to Settings, which lists them); raised again only when the problems change, taken away when fixed.
    /// </summary>
    private void ReportSettingsProblems(SettingsStore store)
    {
        const string tag = "settings-problems";
        var key = ConfigProblems.Key(store.SettingsError, store.SettingsProblems);
        if (key == _settingsNoticeKey || _presenter?.Notices is not { } notices) return;
        _settingsNoticeKey = key;
        if (store.SettingsError is { } error) _log?.Error(error);
        foreach (var problem in store.SettingsProblems) _log?.Info($"settings: {problem}");
        var openSettings = (Strings.Bar_OpenSettings, new Action(() => _settingsHost?.Show()));
        if (key.Length == 0) notices.Remove(tag);
        else if (store.SettingsError is { } unreadable)
            notices.ShowTagged(tag, Strings.Settings_NoticeUnreadableTitle, $"{unreadable} {Strings.Settings_NoticeKept}",
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error, closable: true, action: openSettings);
        else
            notices.ShowTagged(tag, Strings.Settings_NoticeProblemsTitle,
                L.F(Strings.Settings_NoticeProblems, ConfigProblems.Summary(store.SettingsProblems, 5, Strings.Settings_AndMore)),
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning, closable: true, action: openSettings);
    }
}
