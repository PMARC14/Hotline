using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Hotline.App.Chat;

/// <summary>
/// The first time the panel opens: one note saying what is sent where (and two quick tips), with a link to the
/// README's "What leaves your PC". Shown once; a marker file in ~/.hotline remembers it.
/// </summary>
internal sealed class FirstRunNote(PopupWindow popup, NoticeArea notices, string dataDirectory)
{
    private const string Tag = "first-run";
    private static readonly Uri Details = new("https://github.com/PMARC14/hotline#what-leaves-your-pc");
    private string Marker => Path.Combine(dataDirectory, ".first-run-shown");

    public void Initialize() => popup.Shown += ShowOnce;

    private void ShowOnce()
    {
        popup.Shown -= ShowOnce;
        if (File.Exists(Marker)) return;
        try { File.WriteAllText(Marker, DateTimeOffset.Now.ToString("O")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // can't remember it: don't nag every time
        notices.ShowTagged(Tag, "Welcome to Hotline",
            "What you send — your message, attachments, and text you had selected in the app you came from (shown as a chip you " +
            "can remove) — goes only to the AI connection you picked. Tips: type / for quick actions, and /remember to save a note.",
            InfoBarSeverity.Informational, closable: true, ("What's sent", () => _ = Launcher.LaunchUriAsync(Details)));
    }
}
