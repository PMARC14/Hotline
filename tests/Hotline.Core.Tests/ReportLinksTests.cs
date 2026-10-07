using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class ReportLinksTests
{
    [Theory]
    [InlineData(BackendType.ClaudeCode, "Anthropic")]
    [InlineData(BackendType.Anthropic, "Anthropic")]
    [InlineData(BackendType.Antigravity, "Google")]
    [InlineData(BackendType.Gemini, "Google")]
    public void Anthropic_and_Google_answers_link_to_their_report_page(BackendType type, string provider)
    {
        var target = ReportLinks.For(type);
        Assert.NotNull(target);
        Assert.Equal(provider, target.Provider);
        Assert.Equal("https", target.Page.Scheme);
    }

    [Theory]
    [InlineData(BackendType.OpenAiCompatible)]
    [InlineData(BackendType.Local)]
    public void Other_connections_have_no_report_page_yet(BackendType type) => Assert.Null(ReportLinks.For(type));

    [Fact]
    public async Task Answers_say_which_connection_wrote_them()
    {
        var started = new List<AssistantStarted>();
        var log = new FileLog(Path.Combine(Path.GetTempPath(), $"hotline-report-{Guid.NewGuid():N}.log"));
        var chat = new ChatController(id => id == "fake" ? new FakeBackend(new ChatDelta("ok")) : null, null, new ManualTimeProvider(), log) { BackendId = "fake" };
        chat.Event += e => { if (e is AssistantStarted s) started.Add(s); };
        await chat.SendAsync("hi", []);
        Assert.Equal("fake", Assert.Single(started).BackendId);
    }
}
