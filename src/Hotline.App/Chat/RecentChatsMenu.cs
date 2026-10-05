using Hotline.Core.Chat;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Chat;

/// <summary>The 🕘 menu of recent conversations (and Ctrl+↑ back to the last one).</summary>
internal sealed class RecentChatsMenu(PopupWindow popup, ChatController chat, NoticeArea notices, string dataDirectory)
{
    /// <summary>Recent conversations from the history (set by the app; null when history is off).</summary>
    public Func<int, IReadOnlyList<HistoryStore.Summary>>? Source { get; set; }

    public void Initialize() => popup.RecentMenu.Opening += (_, _) => Build();

    /// <summary>Reopens the most recent conversation other than the current one.</summary>
    public void ResumeLast()
    {
        if (Source?.Invoke(5).FirstOrDefault(c => c.Id != chat.ConversationId) is { } last) Resume(last.Id);
    }

    private void Resume(string id)
    {
        if (chat.IsBusy) { notices.Show("Wait for the answer to finish (or stop it) first.", InfoBarSeverity.Informational); return; }
        if (!chat.Resume(id)) notices.Show("That conversation couldn't be opened.", InfoBarSeverity.Warning);
    }

    private void Build()
    {
        popup.RecentMenu.Items.Clear();
        var recent = Source?.Invoke(10) ?? [];
        if (recent.Count == 0)
        {
            popup.RecentMenu.Items.Add(new MenuFlyoutItem { Text = Source is null ? "Chat history is turned off" : "No saved chats yet", IsEnabled = false });
            return;
        }
        foreach (var c in recent)
        {
            var item = new MenuFlyoutItem { Text = $"{c.Title}   ·   {Ago(c.LastAt)}", IsEnabled = c.Id != chat.ConversationId };
            ToolTipService.SetToolTip(item, $"{c.MessageCount} messages, last {c.LastAt.ToLocalTime():g}");
            item.Click += (_, _) => Resume(c.Id);
            popup.RecentMenu.Items.Add(item);
        }
        popup.RecentMenu.Items.Add(new MenuFlyoutSeparator());
        var folder = new MenuFlyoutItem { Text = "Open history folder" };
        folder.Click += (_, _) => { popup.HidePopup(); CliRunner.OpenFolder(Path.Combine(dataDirectory, "history")); };
        popup.RecentMenu.Items.Add(folder);
    }

    private static string Ago(DateTimeOffset at)
    {
        var span = DateTimeOffset.Now - at;
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} h ago"
            : span.TotalDays < 7 ? $"{(int)span.TotalDays} d ago"
            : at.ToLocalTime().ToString("d MMM");
    }
}
