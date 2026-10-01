using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public class AgyTurnParserTests
{
    // Shapes recorded from agy 1.2.14 on 2026-10-02.
    private const string Init = """{"event":"init","conversation_id":"c","init":{"cwd":"C:\\w","tools":["view_file"]}}""";
    private static string UserStep() => """{"event":"step_update","step_update":{"conversation_id":"c","step_index":0,"state":"DONE","step_type":"user_input"}}""";
    private static string Text(int step, string state, string delta) =>
        "{\"event\":\"step_update\",\"step_update\":{\"conversation_id\":\"c\",\"step_index\":" + step + ",\"state\":\"" + state
        + "\",\"step_type\":\"agent_response\",\"text_delta\":" + System.Text.Json.JsonSerializer.Serialize(delta) + "}}";
    private static string Tool(int step, string state) =>
        "{\"event\":\"step_update\",\"step_update\":{\"conversation_id\":\"c\",\"step_index\":" + step + ",\"state\":\"" + state
        + "\",\"step_type\":\"tool\",\"tool_name\":\"view_file\"}}";
    private static string Result(string status, string response, string? error = null, string? denied = null)
    {
        var json = "{\"event\":\"result\",\"result\":{\"conversation_id\":\"c\",\"status\":\"" + status + "\",\"response\":"
                   + System.Text.Json.JsonSerializer.Serialize(response);
        if (error is not null) json += ",\"error\":" + System.Text.Json.JsonSerializer.Serialize(error);
        if (denied is not null) json += ",\"denied_actions\":[{\"action\":\"" + denied + "\",\"display_name\":\"ViewFile\"}]";
        return json + ",\"usage\":{\"input_tokens\":1}}}";
    }

    private static List<ChatDelta> FeedAll(AgyTurnParser p, params string[] lines) => lines.SelectMany(p.Feed).ToList();

    [Fact]
    public void Streams_text_deltas_and_completes()
    {
        var p = new AgyTurnParser();
        var d = FeedAll(p, Init, UserStep(), Text(1, "ACTIVE", "Hel"), Text(1, "DONE", "lo\n"), Result("SUCCESS", "Hello\n"));
        Assert.Equal(["Hel", "lo\n"], d.Select(x => x.Text));
        Assert.All(d, x => Assert.False(x.ResetBefore));
        Assert.True(p.Completed);
        Assert.Null(p.Error);
    }

    [Fact]
    public void New_response_step_restarts_answer()
    {
        var p = new AgyTurnParser();
        var d = FeedAll(p, Text(1, "DONE", "draft"), Tool(2, "DONE"), Text(3, "ACTIVE", "final"), Result("SUCCESS", "final"));
        Assert.Equal(new ChatDelta("final", ResetBefore: true), d[1]);
    }

    [Fact]
    public void Result_response_wins_when_streamed_text_differs()
    {
        var p = new AgyTurnParser();
        var d = FeedAll(p, Text(1, "DONE", "partial"), Result("SUCCESS", "The full answer\n"));
        Assert.Equal(new ChatDelta("The full answer\n", ResetBefore: true), d[^1]);
    }

    [Fact]
    public void Result_without_streamed_text_emits_response()
    {
        var p = new AgyTurnParser();
        Assert.Equal([new ChatDelta("7391\n")], FeedAll(p, Tool(1, "DONE"), Result("SUCCESS", "7391\n")));
    }

    [Fact]
    public void Error_result_sets_error()
    {
        var p = new AgyTurnParser();
        FeedAll(p, Result("ERROR", "", "stream input message is missing the \"event\" field"));
        Assert.True(p.Completed);
        Assert.Contains("missing the \"event\" field", p.Error);
    }

    [Fact]
    public void Denied_action_with_empty_response_is_an_error()
    {
        var p = new AgyTurnParser();
        FeedAll(p, Result("SUCCESS", "", denied: "read_file"));
        Assert.Contains("read_file", p.Error);
    }

    [Fact]
    public void Non_json_and_unknown_lines_are_ignored()
    {
        var p = new AgyTurnParser();
        Assert.Empty(FeedAll(p, "warning: something", "", """{"event":"heartbeat"}"""));
        Assert.False(p.Completed);
    }
}
