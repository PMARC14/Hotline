using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

/// <summary>What pressing Send does with the typed text, decided in one place.</summary>
public sealed class SendRouterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private SendRouter Router()
    {
        var actions = new QuickActions(Path.Combine(_dir, "actions"));
        actions.EnsureDefaults();
        return new SendRouter(actions);
    }

    [Fact]
    public void Plain_text_is_sent_as_typed() =>
        Assert.Equal(new SendRoute.Message("hello", null), Router().Route("  hello ", hasAttachments: false));

    [Fact]
    public void Nothing_to_send() => Assert.IsType<SendRoute.Nothing>(Router().Route("  ", hasAttachments: false));

    [Fact]
    public void Attachments_alone_are_sent() =>
        Assert.Equal(new SendRoute.Message("", null), Router().Route("", hasAttachments: true));

    [Fact]
    public void Remember_saves_instead_of_sending()
    {
        Assert.Equal(new SendRoute.Remember("I like tea"), Router().Route("/remember I like tea", hasAttachments: true));
        Assert.Equal(new SendRoute.Remember(""), Router().Route("/remember", hasAttachments: false));
    }

    [Fact]
    public void Resume_reopens_a_chat_instead_of_sending()
    {
        Assert.Equal(new SendRoute.Resume(""), Router().Route("/resume", hasAttachments: false));
        Assert.Equal(new SendRoute.Resume("tax forms"), Router().Route("/RESUME  tax forms ", hasAttachments: false));
        Assert.IsType<SendRoute.Message>(Router().Route("/resumes are hard", hasAttachments: false)); // not the command
    }

    [Theory]
    [InlineData("/resume ", true, "")]           // the space after the command opens the list
    [InlineData("/RESUME tax", true, "tax")]     // words already typed (or pasted) carry over
    [InlineData("/resume", false, "")]           // still typing the command
    [InlineData("/resumes", false, "")]
    [InlineData("hello /resume ", false, "")]
    public void Typing_resume_and_a_space_opens_the_list(string text, bool opens, string query)
    {
        Assert.Equal(opens, ResumeCommand.TryTyping(text, out var q));
        Assert.Equal(query, q);
    }

    [Fact]
    public void Resume_is_listed_with_the_quick_actions() =>
        Assert.Contains(QuickActions.BuiltIns, a => a.Name == "resume");

    [Fact]
    public void A_quick_action_expands_and_names_itself()
    {
        var route = Assert.IsType<SendRoute.Message>(Router().Route("/fix teh cat", hasAttachments: false));
        Assert.EndsWith("teh cat", route.Text);
        Assert.Equal("fix", route.Action);
    }

    [Fact]
    public void A_quick_action_with_nothing_to_work_on_asks_for_text() =>
        Assert.Equal(new SendRoute.NeedsText("summarize"), Router().Route("/summarize", hasAttachments: false));

    [Fact]
    public void Plain_text_is_sent_as_it_is_even_when_it_looks_like_a_command()
    {
        Assert.Equal(new SendRoute.Message("/remember my PIN", null), SendRouter.Plain(" /remember my PIN ", hasAttachments: false));
        Assert.Equal(new SendRoute.Message("/fix teh", null), SendRouter.Plain("/fix teh", hasAttachments: false));
        Assert.IsType<SendRoute.Nothing>(SendRouter.Plain("  ", hasAttachments: false));
        Assert.Equal(new SendRoute.Message("", null), SendRouter.Plain("", hasAttachments: true));
    }
}
