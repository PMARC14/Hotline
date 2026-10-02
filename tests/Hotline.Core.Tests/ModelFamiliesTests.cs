using Hotline.Core.Backends;

namespace Hotline.Core.Tests;

public class ModelFamiliesTests
{
    // Real `agy models` output (agy 1.2.14).
    private static readonly IReadOnlyList<ModelInfo> Agy = ModelListParsers.Agy("""
        Fetching available models...
        gemini-3.8-flash-high	Gemini 3.8 Flash (High)
        gemini-3.8-flash-medium	Gemini 3.8 Flash (Medium)
        gemini-3.8-flash-low	Gemini 3.8 Flash (Low)
        gemini-3.1-pro-high	Gemini 3.1 Pro (High)
        gemini-3.1-pro-low	Gemini 3.1 Pro (Low)
        claude-sonnet-4-6	Claude Sonnet 4.6 (Thinking)
        gpt-oss-120b-medium	GPT-OSS 120B (Medium)
        """);

    private static IReadOnlyList<ModelFamily> F => ModelFamilies.Group(Agy);

    [Fact]
    public void Groups_effort_variants_under_clean_names_in_list_order()
        => Assert.Equal(["Gemini 3.8 Flash", "Gemini 3.1 Pro", "Claude Sonnet 4.6 (Thinking)", "GPT-OSS 120B"], F.Select(f => f.Name));

    [Fact]
    public void Levels_are_only_those_the_model_has_in_low_to_high_order()
    {
        Assert.Equal(["low", "medium", "high"], F[0].Levels);
        Assert.Equal(["low", "high"], F[1].Levels);
        Assert.False(F[2].HasLevels);
        Assert.Equal(["medium"], F[3].Levels);
    }

    [Fact]
    public void Locate_finds_family_and_level_of_a_stored_id()
    {
        var hit = ModelFamilies.Locate(F, "gemini-3.1-pro-low");
        Assert.Equal(("Gemini 3.1 Pro", "low"), (hit!.Value.Family.Name, hit.Value.Level));
        Assert.Null(ModelFamilies.Locate(F, "unknown-model"));
    }

    [Theory]
    [InlineData(0, "high", "gemini-3.8-flash-high")]
    [InlineData(1, "medium", "gemini-3.1-pro-high")] // Pro has no medium: nearest, preferring higher
    [InlineData(1, "max", "gemini-3.1-pro-high")]
    [InlineData(2, "high", "claude-sonnet-4-6")]     // no levels: the one model
    [InlineData(0, null, "gemini-3.8-flash-medium")] // no level chosen: the middle one
    public void Resolve_always_gives_a_real_id(int family, string? level, string expected)
        => Assert.Equal(expected, ModelFamilies.Resolve(F[family], level));
}
