using Hotline.Core.Settings;
using Hotline.Core.State;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Hotline.App.Chat;

/// <summary>
/// Once: "Enjoying Hotline?" with GitHub Sponsors and Ko-fi buttons, at the top of the panel the next time it opens after
/// an answer (never in the middle of one). state.json (ShownFlag.SupportNote) remembers that it was shown;
/// general.supportReminder is only the user's on/off preference.
/// </summary>
internal sealed class SupportNote(PopupWindow popup, NoticeArea notices, HotlineSettings settings, StateStore state)
{
    private const string Tag = "support";
    private bool _due;

    public void Initialize() => popup.Shown += ShowIfDue;

    /// <summary>Call when a real answer completes: the note may show the next time the panel opens.</summary>
    public void AnswerCompleted()
    {
        if (Armed) _due = true;
    }

    private bool Armed => SupportLinks.DonationsLive && settings.General.SupportReminder && !state.IsShown(ShownFlag.SupportNote);

    private void ShowIfDue()
    {
        if (!_due || !Armed) return;
        _due = false;
        notices.ShowTagged(Tag, Strings.SupportNote_Title, Strings.SupportNote_Body, InfoBarSeverity.Informational, closable: true,
            ("GitHub Sponsors", () => _ = Launcher.LaunchUriAsync(SupportLinks.GitHubSponsors)),
            ("Ko-fi", () => _ = Launcher.LaunchUriAsync(SupportLinks.KoFi)));
        state.MarkShown(ShownFlag.SupportNote); // after it is on screen
    }
}
