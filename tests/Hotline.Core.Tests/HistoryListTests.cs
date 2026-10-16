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

    [Fact]
    public void Oldest_at_the_top_newest_at_the_bottom()
        => Assert.Equal(["c1", "c2", "c3", "c4"], Ids(HistoryList.Build(Recent, currentId: "c4", query: "")));

    [Fact]
    public void Opens_on_the_newest_chat_other_than_the_open_one()
    {
        var v = HistoryList.Build(Recent, currentId: "c4", query: "");
        Assert.Equal("c3", v.Items[v.Selected].Id);
    }

    [Fact]
    public void With_no_chat_open_the_newest_is_selected()
    {
        var v = HistoryList.Build(Recent, currentId: null, query: "");
        Assert.Equal("c4", v.Items[v.Selected].Id);
    }

    [Theory]
    [InlineData("tax", new[] { "c1", "c3" }, "c3")]          // every match, newest selected
    [InlineData("TAX question", new[] { "c1" }, "c1")]       // every word, any case, any order
    [InlineData("  pasta  ", new[] { "c2" }, "c2")]
    public void Search_words_filter_the_list(string query, string[] expected, string selected)
    {
        var v = HistoryList.Build(Recent, currentId: "c4", query);
        Assert.Equal(expected, Ids(v));
        Assert.Equal(selected, v.Items[v.Selected].Id);
    }

    [Fact]
    public void Nothing_matches()
    {
        var v = HistoryList.Build(Recent, currentId: "c4", query: "recipe for tax");
        Assert.Empty(v.Items);
        Assert.Equal(-1, v.Selected);
    }

    [Fact]
    public void Only_the_open_chat_matches_it_is_still_listed_and_selected()
    {
        var v = HistoryList.Build(Recent, currentId: "c4", query: "current");
        Assert.Equal(["c4"], Ids(v));
        Assert.Equal(0, v.Selected);
    }
}
