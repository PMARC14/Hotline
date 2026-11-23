using Hotline.Core.State;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Hotline.App.Chat;

/// <summary>
/// The first time the panel opens: one note saying what is sent where (and two quick tips), with a link to the
/// README's "What leaves your PC". Shown once; state.json (ShownFlag.FirstRun) remembers it.
/// </summary>
internal sealed class FirstRunNote(PopupWindow popup, NoticeArea notices, StateStore state)
{
    private const string Tag = "first-run";
    private static readonly Uri Details = new("https://github.com/PMARC14/hotline#what-leaves-your-pc");

    public void Initialize() => popup.Shown += ShowOnce;

    private void ShowOnce()
    {
        popup.Shown -= ShowOnce;
        if (state.IsShown(ShownFlag.FirstRun)) return;
        notices.ShowTagged(Tag, Strings.Welcome_Title, Strings.Welcome_Body,
            InfoBarSeverity.Informational, closable: true, (Strings.Welcome_WhatsSent, () => _ = Launcher.LaunchUriAsync(Details)));
        state.MarkShown(ShownFlag.FirstRun); // after it is on screen; if state.json can't be written it shows again next start
    }
}
