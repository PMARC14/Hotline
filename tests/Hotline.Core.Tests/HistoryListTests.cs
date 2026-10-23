using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

/// <summary>The history list: oldest at the top, newest at the bottom, filtered by the search words.</summary>
public class HistoryListTests
{
    // Newest first, as HistoryStore.Recent returns them.
    private static readonly HistoryStore.Summary[] Recent =
    [
        new("c4", "Current chat", DateTimeOffset.Now, 2),
        new("c3", "Tax forms for 2026", DateTimeOffset.Now.AddHours(-1), 6),
        new("c2", "Pasta recipe", DateTimeOffset.Now.AddDays(-1), 4),
        new("c1", "Old tax question", DateTimeOffset.Now.AddDays(-3), 4),
    ];

    private static string[] Ids(HistoryList.View v) => [.. v.Items.Select(i => i.Id)];

    // Oldest at the top, newest at the bottom; the selection opens on the newest chat other than the open one.
    [Theory]
    [InlineData("", "c4", new[] { "c1", "c2", "c3", "c4" }, "c3")]
    [InlineData("", null, new[] { "c1", "c2", "c3", "c4" }, "c4")]               // no chat open: the newest
    [InlineData("tax", "c4", new[] { "c1", "c3" }, "c3")]                        // every match, newest selected
    [InlineData("TAX question", "c4", new[] { "c1" }, "c1")]                     // every word, any case, any order
    [InlineData("  pasta  ", "c4", new[] { "c2" }, "c2")]
    [InlineData("recipe for tax", "c4", new string[0], null)]                    // nothing matches
    [InlineData("current", "c4", new[] { "c4" }, "c4")]                          // the open chat is still listed and selected
    public void Build_orders_filters_and_selects(string query, string? currentId, string[] expected, string? selected)
    {
        var v = HistoryList.Build(Recent, currentId, query);
        Assert.Equal(expected, Ids(v));
        if (selected is null) Assert.Equal(-1, v.Selected);
        else Assert.Equal(selected, v.Items[v.Selected].Id);
    }
}
