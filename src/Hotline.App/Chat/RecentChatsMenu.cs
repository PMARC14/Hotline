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
        if (chat.IsBusy) { notices.Show(Strings.Recent_Busy, InfoBarSeverity.Informational); return; }
        if (!chat.Resume(id)) notices.Show(Strings.Recent_OpenFailed, InfoBarSeverity.Warning);
    }

    private void Build()
    {
        popup.RecentMenu.Items.Clear();
        var recent = Source?.Invoke(10) ?? [];
        if (recent.Count == 0)
        {
            popup.RecentMenu.Items.Add(new MenuFlyoutItem { Text = Source is null ? Strings.Recent_HistoryOff : Strings.Recent_None, IsEnabled = false });
            return;
        }
        foreach (var c in recent)
        {
            var item = new MenuFlyoutItem { Text = $"{c.Title}   ·   {Ago(c.LastAt)}", IsEnabled = c.Id != chat.ConversationId };
            ToolTipService.SetToolTip(item, L.F(Strings.Recent_Tip, c.MessageCount, c.LastAt.ToLocalTime()));
            item.Click += (_, _) => Resume(c.Id);
            popup.RecentMenu.Items.Add(item);
        }
        popup.RecentMenu.Items.Add(new MenuFlyoutSeparator());
        var folder = new MenuFlyoutItem { Text = Strings.Recent_OpenFolder };
        folder.Click += (_, _) => { popup.HidePopup(); CliRunner.OpenFolder(Path.Combine(dataDirectory, "history")); };
        popup.RecentMenu.Items.Add(folder);
    }

    private static string Ago(DateTimeOffset at)
    {
        var span = DateTimeOffset.Now - at;
        return span.TotalMinutes < 1 ? Strings.Recent_JustNow
            : span.TotalHours < 1 ? L.F(Strings.Recent_MinutesAgo, (int)span.TotalMinutes)
            : span.TotalDays < 1 ? L.F(Strings.Recent_HoursAgo, (int)span.TotalHours)
            : span.TotalDays < 7 ? L.F(Strings.Recent_DaysAgo, (int)span.TotalDays)
            : at.ToLocalTime().ToString(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.MonthDayPattern, System.Globalization.CultureInfo.CurrentCulture);
    }
}
