using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Hotline.App.Chat;

/// <summary>
/// Once, after the first answer: Hotline is free, donations keep it going (GitHub Sponsors or Ko-fi). A marker file in
/// ~/.hotline remembers it was shown; Settings › About keeps the links.
/// </summary>
internal sealed class SupportNote(NoticeArea notices, string dataDirectory)
{
    private const string Tag = "support";
    private string Marker => Path.Combine(dataDirectory, ".support-note-shown");
    private bool _done;

    /// <summary>Call when an answer completes; shows the note the first time ever.</summary>
    public void AnswerCompleted()
    {
        if (_done || !SupportLinks.DonationsLive) return;
        _done = true;
        if (File.Exists(Marker)) return;
        try { File.WriteAllText(Marker, DateTimeOffset.Now.ToString("O")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // can't remember it: don't nag
        notices.ShowTagged(Tag, "Enjoying Hotline?",
            "It's free and open source, made by one person. If it helps you, a donation keeps it going (GitHub Sponsors or " +
            "Ko-fi; links stay in Settings › About). This note won't show again.",
            InfoBarSeverity.Informational, closable: true, ("Support Hotline", () => _ = Launcher.LaunchUriAsync(SupportLinks.GitHubSponsors)));
    }
}
