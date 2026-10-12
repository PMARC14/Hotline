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
        notices.ShowTagged(Tag, Strings.Welcome_Title, Strings.Welcome_Body,
            InfoBarSeverity.Informational, closable: true, (Strings.Welcome_WhatsSent, () => _ = Launcher.LaunchUriAsync(Details)));
    }
}
