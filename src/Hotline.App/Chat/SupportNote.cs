using Hotline.Core.Settings;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Hotline.App.Chat;

/// <summary>
/// Once: "Enjoying Hotline?" with GitHub Sponsors and Ko-fi buttons, at the top of the panel the next time it opens after
/// an answer (never in the middle of one). general.supportReminder turns itself off once shown; Settings › About can turn
/// it back on, and keeps the links.
/// </summary>
internal sealed class SupportNote(PopupWindow popup, NoticeArea notices, HotlineSettings settings, string dataDirectory)
{
    private const string Tag = "support";
    // Before the setting existed (0.2.x), a marker file remembered that the note had been shown.
    private string OldMarker => Path.Combine(dataDirectory, ".support-note-shown");
    private bool _due;

    /// <summary>Saves a settings change; set by App.</summary>
    public Action<Action<HotlineSettings>>? UpdateSettings { get; set; }

    public void Initialize() => popup.Shown += ShowIfDue;

    /// <summary>Call when a real answer completes: the note may show the next time the panel opens.</summary>
    public void AnswerCompleted()
    {
        if (!Armed) return;
        if (File.Exists(OldMarker))
        {
            TurnOff();
            try { File.Delete(OldMarker); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return;
        }
        _due = true;
    }

    private bool Armed => SupportLinks.DonationsLive && settings.General.SupportReminder;

    private void ShowIfDue()
    {
        if (!_due || !Armed || UpdateSettings is null) return;
        _due = false;
        TurnOff();
        notices.ShowTagged(Tag, Strings.SupportNote_Title, Strings.SupportNote_Body, InfoBarSeverity.Informational, closable: true,
            ("GitHub Sponsors", () => _ = Launcher.LaunchUriAsync(SupportLinks.GitHubSponsors)),
            ("Ko-fi", () => _ = Launcher.LaunchUriAsync(SupportLinks.KoFi)));
    }

    private void TurnOff() => UpdateSettings?.Invoke(s => s.General.SupportReminder = false);
}
