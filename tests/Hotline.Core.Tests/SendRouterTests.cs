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
    public void A_quick_action_expands_and_names_itself()
    {
        var route = Assert.IsType<SendRoute.Message>(Router().Route("/fix teh cat", hasAttachments: false));
        Assert.EndsWith("teh cat", route.Text);
        Assert.Equal("fix", route.Action);
    }

    [Fact]
    public void A_quick_action_with_nothing_to_work_on_asks_for_text() =>
        Assert.Equal(new SendRoute.NeedsText("summarize"), Router().Route("/summarize", hasAttachments: false));
}
