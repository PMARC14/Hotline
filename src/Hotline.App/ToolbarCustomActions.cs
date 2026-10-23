using Hotline.App.Chat;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Windowing;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App;

/// <summary>
/// What a custom button (toolbar.json "buttons") does. A quick action puts its instruction, applied to the draft (or the
/// attachments), in the message box, as typing <c>/name</c> before the draft would; a text button puts its text at the
/// caret. Either is sent at once only when <see cref="CustomButton.Send"/> (toolbar.json allows it for text only), as
/// plain text: the file's strings are only ever text in the message box, never markup, paths or commands (a text
/// starting with "/" is sent as it is, not run). Nothing here throws: a missing
/// action is a notice.
/// </summary>
internal sealed class ToolbarCustomActions(Composer composer, QuickActions actions, Func<bool> hasAttachments, NoticeArea notices,
    Action send, FileLog log)
{
    public void Run(CustomButton button)
    {
        try
        {
            if (composer.IsReadOnly) // dictation is writing into the message box
            {
                notices.Show(Strings.Bar_ActionBusy, InfoBarSeverity.Informational);
                return;
            }
            var put = button.QuickAction is { } name ? PutAction(name) : Insert(button.Text);
            if (put && button.Send) send();
        }
        catch (Exception ex) { log.Error($"custom button {button.Id} failed", ex); }
    }

    /// <summary>The action's instruction with the draft, in the message box (the user reads it and presses Enter).</summary>
    private bool PutAction(string name)
    {
        var draft = composer.Text.Trim();
        // A missing or unreadable file, or a name Hotline handles itself (remember, resume), isn't an action file.
        if (actions.Apply(name, draft, hasAttachments()) is not { } expanded)
        {
            log.Info($"custom button: quick action {name} not found");
            notices.Show(L.F(Strings.Bar_ActionMissing, name), InfoBarSeverity.Warning);
            return false;
        }
        if (expanded.Text is not { } text)
        {
            notices.Show(L.F(Strings.Chat_ActionNeedsText, name), InfoBarSeverity.Warning); // no draft and nothing attached
            return false;
        }
        composer.Text = text;
        return true;
    }

    private bool Insert(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        composer.Insert(text);
        return true;
    }
}
