using Hotline.Core.Diagnostics;

namespace Hotline.Core.Tests;

public class ConfigProblemsTests
{
    [Fact]
    public void Few_problems_are_all_listed()
        => Assert.Equal("a; b; c", ConfigProblems.Summary(["a", "b", "c"]));

    [Fact]
    public void A_chosen_number_are_shown_then_the_translated_count()
        => Assert.Equal("a; b (and 3 more)", ConfigProblems.Summary(["a", "b", "c", "d", "e"], 2, "(and {0} more)"));

    [Fact]
    public void Many_problems_show_the_first_three_and_a_count()
        => Assert.Equal("a; b; c (and 2 more)", ConfigProblems.Summary(["a", "b", "c", "d", "e"]));

    [Fact]
    public void Nothing_gives_an_empty_text() => Assert.Equal("", ConfigProblems.Summary([]));

    [Fact]
    public void Each_problem_is_cleaned_like_a_log_line_and_cut()
    {
        var text = ConfigProblems.Summary(["one\nINFO forged", new string('x', 500)]);
        Assert.DoesNotContain('\n', text);
        Assert.True(text.Length < 400);
        Assert.Contains("…", text);
    }

    [Fact]
    public void The_key_changes_with_the_problems_and_not_with_their_order_of_arrival()
    {
        Assert.Equal(ConfigProblems.Key(null, ["a", "b"]), ConfigProblems.Key(null, ["a", "b"]));
        Assert.NotEqual(ConfigProblems.Key(null, ["a", "b"]), ConfigProblems.Key(null, ["a", "c"]));
        Assert.NotEqual(ConfigProblems.Key(null, []), ConfigProblems.Key("couldn't be read", []));
        Assert.Equal("", ConfigProblems.Key(null, []));
    }
}
