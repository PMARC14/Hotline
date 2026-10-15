namespace Hotline.Core.Chat;

/// <summary>
/// What the history list shows (the 🕘 button, Ctrl+↑ and "/resume words" all open it): the saved chats oldest at the
/// top and newest at the bottom, next to the message bar, filtered by the search words (every word in the title, any
/// case). It opens on the newest chat other than the open one.
/// </summary>
public static class HistoryList
{
    /// <param name="Items">Oldest first.</param>
    /// <param name="Selected">Index into <paramref name="Items"/>; -1 when the list is empty.</param>
    public sealed record View(IReadOnlyList<HistoryStore.Summary> Items, int Selected);

    public static View Build(IEnumerable<HistoryStore.Summary> newestFirst, string? currentId, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var items = newestFirst.Where(c => words.All(w => c.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase))).Reverse().ToList();
        var selected = items.FindLastIndex(c => c.Id != currentId);
        if (selected < 0) selected = items.Count - 1; // only the open chat matched (or the list is empty: -1)
        return new View(items, selected);
    }
}
