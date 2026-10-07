using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public sealed class QuickActionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"), "actions");
    public void Dispose() { if (Directory.Exists(Path.GetDirectoryName(_dir))) Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true); }

    [Fact]
    public void First_run_writes_the_four_defaults_once()
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        Assert.Equal(["explain", "fix", "summarize", "translate"], actions.List().Select(a => a.Name));
        File.Delete(Path.Combine(_dir, "fix.md"));
        actions.EnsureDefaults(); // a deleted default stays deleted
        Assert.DoesNotContain(actions.List(), a => a.Name == "fix");
    }

    [Fact]
    public void Users_add_actions_by_dropping_in_files_and_they_apply_live()
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        File.WriteAllText(Path.Combine(_dir, "tone.md"), "\n\nMake the text below friendlier.\nKeep it short.");
        var tone = Assert.Single(actions.List(), a => a.Name == "tone");
        Assert.Equal("Make the text below friendlier.", tone.Description);
        Assert.Equal("Make the text below friendlier.\nKeep it short.", tone.Instruction);
    }

    [Fact]
    public void Empty_unsafe_or_non_md_files_are_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "blank.md"), "  \n");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "two words.md"), "x");
        Assert.Empty(new QuickActions(_dir).List());
    }

    [Fact]
    public void A_missing_folder_lists_nothing()
    {
        Assert.Empty(new QuickActions(_dir).List());
    }

    [Theory]
    [InlineData("/tr", "tr")]
    [InlineData("/", "")]
    [InlineData("/Translate", "Translate")]
    public void Suggestion_query_is_the_name_being_typed(string text, string query) =>
        Assert.Equal(query, QuickActions.SuggestionQuery(text));

    [Theory]
    [InlineData("hello")]
    [InlineData("/translate hello")] // past the name: no list
    [InlineData(" /tr")]
    [InlineData("/a/b")]
    public void No_suggestions_outside_a_leading_command(string text) => Assert.Null(QuickActions.SuggestionQuery(text));

    [Fact]
    public void Matches_by_prefix_case_insensitively()
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        Assert.Equal(["summarize"], actions.Matching("SU").Select(a => a.Name));
        Assert.Equal(5, actions.Matching("").Count); // the four defaults + the built-in /remember
    }

    [Fact]
    public void Expands_the_message_after_the_command()
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        var result = actions.Expand("/fix  teh cat sat\nsecond line", hasAttachments: false);
        Assert.NotNull(result);
        Assert.Equal("fix", result!.Value.Action.Name);
        Assert.Equal(actions.List().Single(a => a.Name == "fix").Instruction + "\n\n---\n\nteh cat sat\nsecond line", result.Value.Text);
    }

    [Fact]
    public void An_empty_message_applies_the_action_to_the_attachments()
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        var result = actions.Expand("/summarize", hasAttachments: true);
        Assert.Equal(actions.List().Single(a => a.Name == "summarize").Instruction + "\n\nApply this to the attached content.", result!.Value.Text);
    }

    [Fact]
    public void An_empty_message_with_nothing_attached_needs_text()
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        var result = actions.Expand("/explain ", hasAttachments: false);
        Assert.NotNull(result);
        Assert.Null(result!.Value.Text);
    }

    [Theory]
    [InlineData("/unknown hi")]
    [InlineData("hi /fix")]
    [InlineData("/fixit now")]
    public void Anything_else_is_sent_unchanged(string text)
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        Assert.Null(actions.Expand(text, hasAttachments: false));
    }

    [Fact]
    public void Edits_apply_on_the_next_use_even_with_the_list_cached()
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        _ = actions.List();
        var path = Path.Combine(_dir, "fix.md");
        File.WriteAllText(path, "Fix it differently.");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1)); // file systems with coarse timestamps
        Assert.Equal("Fix it differently.", actions.List().Single(a => a.Name == "fix").Instruction);
    }

    [Fact]
    public void Oversized_files_are_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "huge.md"), new string('x', QuickActions.MaxFileBytes + 1));
        Assert.Empty(new QuickActions(_dir).List());
    }

    [Fact]
    public void A_file_locked_during_a_read_is_picked_up_on_the_next_use()
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        using (new FileStream(Path.Combine(_dir, "fix.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.DoesNotContain(actions.List(), a => a.Name == "fix");
        Assert.Contains(actions.List(), a => a.Name == "fix");
    }

    [Fact]
    public void Create_and_delete_from_settings()
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        var name = actions.Create();
        Assert.Equal("my-action", name);
        Assert.Equal("my-action-2", actions.Create());
        Assert.Contains(actions.List(), a => a.Name == "my-action");
        Assert.True(File.Exists(actions.PathFor("my-action")));
        Assert.True(actions.Delete("my-action"));
        Assert.False(actions.Delete("../escape"));
        Assert.DoesNotContain(actions.List(), a => a.Name == "my-action");
    }
}
