using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Codex;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class CodexModelsTests
{
    private static readonly BackendProfile Profile = new() { Id = "cx", Type = BackendType.Codex };

    private static string Model(string id, string name, bool hidden = false, string efforts = "low") =>
        $$"""{"id":"{{id}}","model":"{{id}}","displayName":"{{name}}","hidden":{{(hidden ? "true" : "false")}},"supportedReasoningEfforts":[{{string.Join(",", efforts.Split(',').Select(e => $$"""{"reasoningEffort":"{{e}}","description":"d"}"""))}}],"isDefault":false}""";

    /// <summary>A scripted codex app-server: answers initialize, then model/list one page per cursor.</summary>
    private static FakeLineProcess Server(string[][] pages) => new()
    {
        Respond = line =>
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("method", out var m)) return [];
            if (m.GetString() == "initialize") return ["not json", """{"method":"hello","params":{}}""", """{"id":0,"result":{"userAgent":"x"}}"""];
            if (m.GetString() != "model/list") return [];
            var p = root.GetProperty("params");
            var cursor = p.TryGetProperty("cursor", out var c) && c.ValueKind == JsonValueKind.String ? int.Parse(c.GetString()!) : 0;
            var next = cursor + 1 < pages.Length ? $"\"{cursor + 1}\"" : "null";
            return ["""{"method":"note","params":{}}""", $$$"""{"id":{{{root.GetProperty("id").GetRawText()}}},"result":{"data":[{{{string.Join(",", pages[cursor])}}}],"nextCursor":{{{next}}}}}"""];
        },
    };

    private static CodexModelLister Lister(FakeLineProcessFactory f, string? exe = "codex.exe", string? cache = null, TimeSpan? timeout = null) =>
        new(f, _ => exe, () => cache, "1.2.3", timeout);

    [Fact]
    public async Task App_server_lists_models_across_pages_skipping_hidden()
    {
        var f = new FakeLineProcessFactory { Create = () => Server([[Model("gpt-a", "GPT A", efforts: "low,max"), Model("h", "Hidden", hidden: true)], [Model("gpt-b", "GPT B")]]) };
        var models = await Lister(f).ListAsync(Profile, default);
        Assert.Equal(["gpt-a", "gpt-b"], models.Select(m => m.Id));
        Assert.Equal("GPT A", models[0].Label);
        Assert.Equal(["low", "max"], models[0].Efforts);
        var (exe, args, _, proc) = Assert.Single(f.Started);
        Assert.Equal("codex.exe", exe);
        Assert.Equal(["app-server"], args);
        Assert.True(proc.Disposed);
        Assert.Contains("\"clientInfo\"", proc.Written[0]);
        Assert.Contains("\"version\":\"1.2.3\"", proc.Written[0]);
        Assert.Contains("\"method\":\"initialized\"", proc.Written[1]);
        Assert.Contains("\"cursor\":\"1\"", proc.Written[3]);
    }

    [Fact]
    public async Task Model_falls_back_to_id_and_label_to_model()
    {
        var f = new FakeLineProcessFactory { Create = () => Server([["""{"id":"only-id","displayName":"Only"}""", """{"model":"m","id":"x"}"""]]) };
        var models = await Lister(f).ListAsync(Profile, default);
        Assert.Equal(["only-id", "m"], models.Select(m => m.Id));
        Assert.Equal("m", models[1].Label);
    }

    [Fact]
    public async Task Hung_server_times_out_then_uses_cache_and_kills_process()
    {
        var f = new FakeLineProcessFactory(); // never answers
        const string cache = """{"models":[{"slug":"c1","display_name":"Cached"}]}""";
        var models = await Lister(f, cache: cache, timeout: TimeSpan.FromMilliseconds(200)).ListAsync(Profile, default);
        Assert.Equal("c1", Assert.Single(models).Id);
        Assert.True(f.Started[0].Process.Disposed);
    }

    [Fact]
    public async Task Cancelling_the_caller_propagates()
    {
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Lister(new FakeLineProcessFactory()).ListAsync(Profile, cts.Token));
    }

    [Fact]
    public async Task Missing_codex_uses_cache_without_starting_a_process()
    {
        var f = new FakeLineProcessFactory();
        var models = await Lister(f, exe: null, cache: """{"models":[{"slug":"z"}]}""").ListAsync(Profile, default);
        Assert.Empty(f.Started);
        Assert.Equal(["z"], models.Select(m => m.Id));
    }

    [Fact]
    public async Task Process_that_exits_fails_to_start_or_lists_nothing_falls_through_to_curated()
    {
        var f = new FakeLineProcessFactory { Create = () => { var p = new FakeLineProcess(); p.Exit(); return p; } };
        Assert.Equal(CodexModels.Curated, await Lister(f).ListAsync(Profile, default));
        var g = new FakeLineProcessFactory { Create = () => throw new InvalidOperationException("no") };
        Assert.Equal(CodexModels.Curated, await Lister(g, cache: "{").ListAsync(Profile, default));
        var empty = new FakeLineProcessFactory { Create = () => Server([[]]) };
        Assert.Equal(CodexModels.Curated, await Lister(empty).ListAsync(Profile, default));
    }

    [Fact]
    public void Cache_top_level_or_wrapped_filters_visibility_and_sorts_by_priority()
    {
        const string json = """
            {"fetched_at":"x","data":{"models":[
              {"slug":"b","display_name":"B","visibility":"list","priority":2},
              {"slug":"a","display_name":"A","priority":1,"supported_reasoning_levels":[{"effort":"low"},{"effort":"ultra"}]},
              {"slug":"hid","visibility":"hide","priority":0},
              {"display_name":"noslug"}]}}
            """;
        var models = CodexModels.ParseCache(json)!;
        Assert.Equal(["a", "b"], models.Select(m => m.Id));
        Assert.Equal("A", models[0].Label);
        Assert.Equal(["low", "ultra"], models[0].Efforts);
        var plain = CodexModels.ParseCache("""{"models":[{"slug":"x"}]}""")!;
        Assert.Equal("x", plain[0].Id);
        Assert.Equal("x", plain[0].Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"models":5}""")]
    [InlineData("""{"models":[]}""")]
    [InlineData("""{"other":1}""")]
    [InlineData("[1,2]")]
    public void Unusable_cache_gives_null(string json) => Assert.Null(CodexModels.ParseCache(json));

    // The shipped list and the effort levels Codex accepts: pinned data (a change should be deliberate).
    [Fact]
    public void The_curated_list_starts_with_the_default_and_the_levels_include_max_and_ultra()
    {
        Assert.Equal("gpt-6.1-sol", CodexModels.Curated[0].Id);
        Assert.Equal(8, CodexModels.Curated.Count);
        Assert.True(ConnectionTypes.Of(BackendType.Codex).ListsModels);
        Assert.Contains("max", CodexProtocol.EffortLevels);
        Assert.Contains("ultra", CodexProtocol.EffortLevels);
    }
}
