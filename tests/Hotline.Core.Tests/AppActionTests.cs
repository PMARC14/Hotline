using System.Text.Json;
using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

/// <summary>App Actions on Windows: Click to Do and other apps launch hotline-action://… with the selected content.</summary>
public sealed class AppActionTests
{
    private const string Windows = "MicrosoftWindows.Client.CBS_cw5n1h2txyewy";
    private static Dictionary<string, string> Inputs(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Ask_about_text_attaches_it_without_sending()
    {
        var a = AppActionRequest.Parse(new Uri("hotline-action://ask"), Inputs(("text", "Some selected text")), Windows);
        Assert.NotNull(a);
        Assert.Equal("Some selected text", a!.Text);
        Assert.Null(a.QuickAction);
        Assert.False(a.AutoSend);
    }

    [Fact]
    public void A_quick_action_from_windows_runs_at_once()
    {
        var a = AppActionRequest.Parse(new Uri("hotline-action://run/summarize"), Inputs(("text", "long text")), Windows)!;
        Assert.Equal("summarize", a.QuickAction);
        Assert.True(a.FromWindows);
        Assert.True(a.AutoSend);
    }

    [Fact]
    public void Other_callers_never_auto_send() // any app can launch a URI; it mustn't spend the user's AI on its own
    {
        Assert.False(AppActionRequest.Parse(new Uri("hotline-action://run/summarize"), Inputs(("text", "x")), "Contoso.App_abc123")!.AutoSend);
        Assert.False(AppActionRequest.Parse(new Uri("hotline-action://run/summarize?text=x"), null, null)!.AutoSend);
    }

    [Fact]
    public void A_plain_launch_can_pass_text_in_the_query() =>
        Assert.Equal("hello world", AppActionRequest.Parse(new Uri("hotline-action://ask?text=hello%20world"), null, null)!.Text);

    [Fact]
    public void Images_must_be_local_png_or_jpeg_files()
    {
        Assert.Equal(@"C:\Users\me\Pictures\shot.png", AppActionRequest.Parse(new Uri("hotline-action://ask"), Inputs(("image", @"C:\Users\me\Pictures\shot.png")), Windows)!.ImagePath);
        Assert.Null(AppActionRequest.Parse(new Uri("hotline-action://ask"), Inputs(("image", @"\\server\share\x.png")), Windows));
        Assert.Null(AppActionRequest.Parse(new Uri("hotline-action://ask"), Inputs(("image", @"C:\x.exe")), Windows));
        Assert.Null(AppActionRequest.Parse(new Uri("hotline-action://ask"), Inputs(("image", "relative.png")), Windows));
    }

    [Theory]
    [InlineData("hotline-action://run/..%2Fevil")]
    [InlineData("hotline-action://run/")]
    [InlineData("hotline-action://unknown")]
    [InlineData("hotline://ask")]
    public void Unknown_or_unsafe_launches_are_ignored(string uri) =>
        Assert.Null(AppActionRequest.Parse(new Uri(uri), Inputs(("text", "x")), Windows));

    [Fact]
    public void Nothing_to_work_on_is_ignored() => Assert.Null(AppActionRequest.Parse(new Uri("hotline-action://ask"), Inputs(), Windows));

    [Fact]
    public void Very_long_text_is_cut() =>
        Assert.True(AppActionRequest.Parse(new Uri("hotline-action://ask"), Inputs(("text", new string('a', 200_000))), Windows)!.Text!.Length <= AppActionRequest.MaxTextChars);

    [Fact]
    public void The_planner_routes_app_actions()
    {
        var request = new ActivationRequest(ActivationKind.ProtocolForResults, new Uri("hotline-action://ask"), false,
            Inputs(("text", "hi")), Windows);
        var plan = ActivationPlanner.Plan(request);
        Assert.Equal("hi", plan.AppAction!.Text);
        Assert.False(plan.ShowPopup); // the action shows it, with the content attached
    }

    [Fact]
    public void The_packaged_action_definitions_match_what_hotline_handles()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Hotline.App", "Public", "actions.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal(3, root.GetProperty("version").GetInt32());
        foreach (var action in root.GetProperty("actions").EnumerateArray())
        {
            Assert.Equal(["*"], action.GetProperty("allowedAppInvokers").EnumerateArray().Select(e => e.GetString()));
            var invocation = action.GetProperty("invocation");
            Assert.Equal("Uri", invocation.GetProperty("type").GetString());
            var uri = new Uri(invocation.GetProperty("uri").GetString()!);
            var inputs = invocation.GetProperty("inputData").EnumerateObject().ToDictionary(p => p.Name, _ => "C:\\x.png");
            Assert.NotNull(AppActionRequest.Parse(uri, inputs, Windows)); // every declared action parses
        }
    }
}
