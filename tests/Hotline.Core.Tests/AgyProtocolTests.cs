using System.Text.Json;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class AgyProtocolTests
{
    [Fact]
    public void User_line_is_single_line_event_json()
    {
        var line = AgyProtocol.UserLine("hi\n\"there\"");
        Assert.DoesNotContain('\n', line);
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("user", doc.RootElement.GetProperty("event").GetString());
        var msg = doc.RootElement.GetProperty("message");
        Assert.Equal("user", msg.GetProperty("role").GetString());
        Assert.Equal("hi\n\"there\"", msg.GetProperty("content").GetString());
    }

    [Fact]
    public void Plain_prompt_is_just_the_text()
        => Assert.Equal("What is 2+2?", AgyProtocol.ComposePrompt("What is 2+2?", [], [], []));

    [Fact]
    public void Images_are_listed_and_text_files_inlined()
    {
        var p = AgyProtocol.ComposePrompt("Compare these", ["attachments/m1/a.png"], [("notes.md", "# Notes")], []);
        Assert.Contains("attachments/m1/a.png", p);
        Assert.Contains("view_file", p);
        Assert.Contains("notes.md", p);
        Assert.Contains("# Notes", p);
        Assert.EndsWith("Compare these", p);
    }

    [Fact]
    public void Attachment_only_prompt_gets_default_question()
        => Assert.EndsWith("Please look at the attached file(s).", AgyProtocol.ComposePrompt("", ["attachments/m1/a.png"], [], []));

    [Fact]
    public void Prior_context_is_replayed_as_transcript()
    {
        var now = DateTimeOffset.UnixEpoch;
        var prior = new List<ChatMessage>
        {
            new("1", ChatRole.User, "Remember PINEAPPLE", [], now),
            new("2", ChatRole.Assistant, "ok", [], now),
        };
        var p = AgyProtocol.ComposePrompt("What word?", [], [], prior);
        Assert.Contains("User: Remember PINEAPPLE", p);
        Assert.Contains("Assistant: ok", p);
        Assert.EndsWith("What word?", p);
    }

    [Fact]
    public void Args_use_stream_json_agent_and_never_skip_permissions()
    {
        var args = AgyProtocol.BuildArgs(new BackendProfile { Id = "agy", Agent = "hotline", Model = "gemini-3.8-flash-low", ExtraArgs = "--effort low" });
        Assert.Equal(["--input-format", "stream-json", "--output-format", "stream-json", "-p=", "--agent", "hotline",
                      "--model", "gemini-3.8-flash-low", "--effort", "low"], args);
        Assert.DoesNotContain("--dangerously-skip-permissions", AgyProtocol.BuildArgs(new BackendProfile { ExtraArgs = "--dangerously-skip-permissions" }));
    }

    [Fact]
    public void Default_agent_is_hotline()
        => Assert.Contains("hotline", AgyProtocol.BuildArgs(new BackendProfile { Id = "agy" }));

    [Fact]
    public void Locator_prefers_configured_then_localappdata_then_path()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\tools\agy.exe", @"C:\L\agy\bin\agy.exe", @"C:\bin\agy.exe" };
        Assert.Equal(@"D:\tools\agy.exe", AgyLocator.Find(@"D:\tools\agy.exe", files.Contains, @"C:\L", @"C:\bin"));
        Assert.Equal(@"C:\L\agy\bin\agy.exe", AgyLocator.Find(null, files.Contains, @"C:\L", @"C:\bin"));
        Assert.Equal(@"C:\bin\agy.exe", AgyLocator.Find(null, files.Contains, @"C:\none", @"C:\x;C:\bin"));
        Assert.Null(AgyLocator.Find(@"D:\missing.exe", files.Contains, @"C:\L", @"C:\bin"));
        Assert.Null(AgyLocator.Find(null, _ => false, @"C:\L", @"C:\bin"));
    }

    [Theory]
    [InlineData("low", true)]
    [InlineData("MAX", true)]
    [InlineData("turbo", false)]
    [InlineData(null, false)]
    public void Effort_is_passed_only_when_valid(string? effort, bool expected)
    {
        var args = AgyProtocol.BuildArgs(new BackendProfile { Id = "agy", Effort = effort });
        Assert.Equal(expected, args.Contains("--effort"));
        if (expected) Assert.Equal(effort!.ToLowerInvariant(), args[args.ToList().IndexOf("--effort") + 1]);
    }

    [Fact]
    public void Inherit_mode_uses_default_agent_and_adds_workspace_dir()
    {
        var args = AgyProtocol.BuildArgs(new BackendProfile { Id = "agy", Agent = "hotline", Tools = ToolMode.Inherit }, addDir: @"C:\ws");
        Assert.DoesNotContain("--agent", args);
        Assert.Equal(@"C:\ws", args[args.ToList().IndexOf("--add-dir") + 1]);
    }

    [Fact]
    public void Instructions_are_prepended_once()
    {
        var p = AgyProtocol.ComposePrompt("hi", [], [], [], instructions: "Be brief.");
        Assert.StartsWith("Instructions for this conversation:", p);
        Assert.Contains("Be brief.", p);
        Assert.EndsWith("hi", p);
    }
}
