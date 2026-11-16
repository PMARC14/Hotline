using System.Security;
using Hotline.App.Interop;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Text;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Hotline.App.Chat;

/// <summary>
/// chat.notifyWhenDone: a Windows notification when an answer finishes (or fails) while the panel is hidden or another app
/// is in front, so a long answer can run in the background. Clicking it opens hotline://answer (the panel, without reading a
/// selection). Opening the panel clears it. Uses the package identity: an unpackaged dev build just logs and skips.
/// </summary>
internal sealed class AnswerNotifier(PopupWindow popup, HotlineSettings settings, FileLog log)
{
    private const string Tag = "answer";
    private const string Group = "hotline";

    public void Initialize()
    {
        popup.Shown += Clear;
        // Also when a pinned panel that was behind another app comes back to the front.
        popup.Activated += (_, e) => { if (e.WindowActivationState != Microsoft.UI.Xaml.WindowActivationState.Deactivated) Clear(); };
    }

    public void Finished(string backendName, string chatTitle, string text, bool failed)
    {
        if (!settings.Chat.NotifyWhenDone) return;
        if (popup.IsShown && Native.GetForegroundWindow() == popup.Hwnd) return; // they're looking at it
        var source = string.IsNullOrWhiteSpace(backendName) ? "Hotline" : backendName;
        string title;
        string body;
        if (settings.Chat.NotifyShowsText)
        {
            title = NotificationText.Snippet(L.F(failed ? Strings.Notify_Failed : Strings.Notify_Answered, source), max: 80);
            body = NotificationText.Snippet(text);
        }
        else
        {
            title = failed ? NotificationText.Snippet(L.F(Strings.Notify_Failed, source), max: 80) : Strings.Notify_AnswerReady;
            body = NotificationText.Snippet(chatTitle, max: 80);
        }
        try
        {
            var bodyXml = string.IsNullOrEmpty(body) ? "" : $"<text>{SecurityElement.Escape(body)}</text>";
            var xml = new XmlDocument();
            xml.LoadXml($"""<toast activationType="protocol" launch="hotline://answer"><visual><binding template="ToastGeneric"><text>{SecurityElement.Escape(title)}</text>{bodyXml}</binding></visual></toast>""");
            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(xml)
            {
                Tag = Tag, Group = Group, ExpirationTime = DateTimeOffset.Now.AddHours(4),
            });
        }
        catch (Exception ex) { log.Error("could not show the answer notification", ex); }
    }

    private void Clear()
    {
        try { ToastNotificationManager.History.Remove(Tag, Group); }
        catch (Exception ex) { log.Debug($"could not clear the answer notification: {ex.Message}"); }
    }
}
