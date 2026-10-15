using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public sealed class QuickActionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"), "actions");
    public void Dispose() { if (Directory.Exists(Path.GetDirectoryName(_dir))) Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true); }

    [Fact]
    public void First_run_writes_the_defaults_once()
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        Assert.Equal(["bullets", "explain", "fix", "formal", "reply", "shorter", "summarize", "translate"], actions.List().Select(a => a.Name));
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
        Assert.Equal(10, actions.Matching("").Count); // the eight defaults + the built-ins /remember and /resume
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

    [Fact]
    public void Restore_adds_missing_defaults_and_updates_untouched_old_ones_but_keeps_edits()
    {
        Directory.CreateDirectory(_dir);
        // An install from before this version: the old four, one of them edited, one deleted.
        File.WriteAllText(Path.Combine(_dir, "translate.md"), QuickActions.PreviousDefaults["translate"][0] + "\n");
        File.WriteAllText(Path.Combine(_dir, "fix.md"), "Fix it my way.\n");
        File.WriteAllText(Path.Combine(_dir, "summarize.md"), QuickActions.DefaultText("summarize") + "\n"); // already current
        var actions = new QuickActions(_dir);

        Assert.Equal(6, actions.RestoreDefaults()); // explain + the four new ones, and translate updated
        var list = actions.List();
        Assert.Equal(["bullets", "explain", "fix", "formal", "reply", "shorter", "summarize", "translate"], list.Select(a => a.Name));
        Assert.Equal("Fix it my way.", list.Single(a => a.Name == "fix").Instruction); // edits are never touched
        Assert.Equal(QuickActions.DefaultText("translate"), list.Single(a => a.Name == "translate").Instruction);
        Assert.Equal(0, actions.RestoreDefaults()); // nothing left to do
    }

    [Fact]
    public void Untouched_defaults_and_built_ins_describe_themselves_in_the_interface_language()
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        File.WriteAllText(Path.Combine(_dir, "fix.md"), "Fix it my way.\nAnd more.\n");
        var all = actions.Matching("");
        Assert.Equal(Hotline.Core.Text.Localization.Strings.Action_translate_Desc, all.Single(a => a.Name == "translate").Description);
        Assert.Equal(Hotline.Core.Text.Localization.Strings.Action_remember_Desc, all.Single(a => a.Name == "remember").Description);
        Assert.Equal(Hotline.Core.Text.Localization.Strings.Action_resume_Desc, all.Single(a => a.Name == "resume").Description);
        Assert.Equal("Fix it my way.", all.Single(a => a.Name == "fix").Description); // edited: its own first line
    }

    [Fact]
    public void Every_default_has_a_translated_description()
    {
        foreach (var name in QuickActions.DefaultNames)
            Assert.False(string.IsNullOrWhiteSpace(Hotline.Core.Text.Localization.L.Get("Action_" + name + "_Desc")), name);
    }

    [Theory]
    [InlineData("fix", "fix-it", true)]
    [InlineData("fix", "explain", false)]   // taken
    [InlineData("fix", "remember", false)]  // a built-in
    [InlineData("fix", "no spaces", false)] // not a file-safe name
    [InlineData("missing", "other", false)]
    public void Rename(string from, string to, bool ok)
    {
        var actions = new QuickActions(_dir);
        actions.EnsureDefaults();
        Assert.Equal(ok, actions.Rename(from, to));
        if (ok) Assert.Contains(actions.List(), a => a.Name == to);
    }
}
