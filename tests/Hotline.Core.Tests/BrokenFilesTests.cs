using Hotline.Core.Chat;
using Hotline.Core.Tools;

namespace Hotline.Core.Tests;

/// <summary>mcp.json, prompts/ and actions/: one bad value or file skips only that item, never the whole list.</summary>
public sealed class BrokenFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public BrokenFilesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void An_mcp_server_with_odd_value_kinds_still_loads_and_the_others_are_untouched()
    {
        var config = McpConfig.Parse("""
            {
              "mcpServers": {
                "good": { "command": "npx", "args": ["-y", 5, null, { "a": 1 }], "env": { "N": 5, "T": true, "Z": null }, "disabled": "true", "cwd": 7, "displayName": ["x"] },
                "noCommand": { "args": [] },
                "numberCommand": { "command": 5 },
                "notAnObject": 5,
                "alsoGood": { "command": "node" }
              },
              "approvals": { "a/b": "allow", "c/d": 3, "e/f": null, "g/h": ["allow"] }
            }
            """);
        Assert.Equal(["good", "alsoGood"], config.Servers.Select(s => s.Name));
        var good = config.Servers[0];
        Assert.Equal("npx", good.Command);
        Assert.False(good.Disabled);   // "true" as text is not a boolean
        Assert.Null(good.WorkingDirectory);
        Assert.Null(good.DisplayName);
        Assert.Equal(4, good.Args.Count);
        Assert.Equal("5", good.Env["N"]);
        Assert.Equal(ToolApproval.Allow, config.Approvals["a/b"]);
        Assert.Equal(ToolApproval.Deny, config.Approvals["c/d"]); // unknown words deny
        Assert.Equal(3, config.Warnings.Count);
    }

    [Theory]
    [InlineData("""{ "mcpServers": [], "approvals": [] }""")]
    [InlineData("""{ "mcpServers": "x", "approvals": 5 }""")]
    [InlineData("""{ "mcpServers": null, "approvals": null }""")]
    [InlineData("{}")]
    public void Wrong_kinds_for_the_two_sections_give_an_empty_but_valid_config(string json)
    {
        var config = McpConfig.Parse(json);
        Assert.Empty(config.Servers);
        Assert.Empty(config.Approvals);
        Assert.Null(config.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{ \"mcpServers\": ")]
    [InlineData("{ \"a\": 1, \"a\": 2 }")] // a key twice: a deliberate error, the file is left as it is
    public void An_unreadable_mcp_file_gives_an_error_and_no_servers_and_is_not_changed(string text)
    {
        var path = Path.Combine(_dir, "mcp.json");
        File.WriteAllText(path, text);
        var config = McpConfig.Load(path);
        Assert.NotNull(config.Error);
        Assert.Empty(config.Servers);
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public void A_locked_prompt_falls_back_to_the_default_and_the_list_survives_odd_names()
    {
        var prompts = new PromptLibrary(Path.Combine(_dir, "prompts"));
        prompts.EnsureDefault();
        File.WriteAllText(prompts.PathFor("mine"), "My prompt");
        File.WriteAllText(Path.Combine(prompts.Directory, "bad;name.md"), "x");
        Directory.CreateDirectory(Path.Combine(prompts.Directory, "folder.md"));
        File.WriteAllBytes(Path.Combine(prompts.Directory, "binary.md"), [0xFF, 0xFE, 0x00, 0x80]);

        Assert.Equal(["default", "binary", "mine"], prompts.List());
        Assert.Equal("My prompt", prompts.Read("mine"));
        using (new FileStream(prompts.PathFor("mine"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(PromptLibrary.DefaultText, prompts.Read("mine")); // an editor holds it: the default, no exception
        Assert.False(string.IsNullOrEmpty(prompts.Read("binary")));
    }

    [Fact]
    public void A_missing_prompt_folder_still_lists_the_default()
    {
        Assert.Equal(["default"], new PromptLibrary(Path.Combine(_dir, "nothing-here")).List());
    }

    [Fact]
    public void One_unreadable_action_file_skips_only_itself()
    {
        var actions = new QuickActions(Path.Combine(_dir, "actions"));
        Directory.CreateDirectory(actions.Directory);
        File.WriteAllText(actions.PathFor("one"), "Do one");
        File.WriteAllText(actions.PathFor("two"), "Do two");
        File.WriteAllText(Path.Combine(actions.Directory, "bad;name.md"), "x");
        File.WriteAllText(actions.PathFor("big"), new string('x', QuickActions.MaxFileBytes + 1));
        File.WriteAllBytes(actions.PathFor("binary"), [0xFF, 0xFE, 0x00, 0x80]);
        Directory.CreateDirectory(Path.Combine(actions.Directory, "folder.md"));

        using (new FileStream(actions.PathFor("two"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var names = actions.List().Select(a => a.Name).ToList();
            Assert.Contains("one", names);
            Assert.DoesNotContain("two", names);
            Assert.DoesNotContain("big", names);
        }
        Assert.Contains("two", actions.List().Select(a => a.Name)); // read again once the lock is gone
    }
}
