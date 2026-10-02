using Hotline.Core.Backends;

namespace Hotline.Core.Tests;

public class ModelListParsersTests
{
    [Fact]
    public void Agy_lines_are_id_tab_label()
        => Assert.Equal(
            [new ModelInfo("gemini-3.8-flash-high", "Gemini 3.8 Flash (High)"), new ModelInfo("claude-sonnet-4-6", "Claude Sonnet 4.6 (Thinking)")],
            ModelListParsers.Agy("Fetching available models...\ngemini-3.8-flash-high\tGemini 3.8 Flash (High)\r\nclaude-sonnet-4-6\tClaude Sonnet 4.6 (Thinking)\n\n"));

    [Fact]
    public void OpenAi_data_ids_sorted()
        => Assert.Equal(["a-model", "b-model"],
            ModelListParsers.OpenAi("""{"object":"list","data":[{"id":"b-model"},{"id":"a-model"}]}""").Select(m => m.Id));

    [Fact]
    public void Gemini_keeps_generate_content_models_without_prefix()
    {
        var models = ModelListParsers.Gemini("""
            {"models":[
              {"name":"models/gemini-3.8-flash","displayName":"Gemini 3.8 Flash","supportedGenerationMethods":["generateContent","countTokens"]},
              {"name":"models/text-embedding-005","displayName":"Embedding","supportedGenerationMethods":["embedContent"]}]}
            """);
        Assert.Equal([new ModelInfo("gemini-3.8-flash", "Gemini 3.8 Flash")], models);
    }

    [Fact]
    public void Anthropic_ids_with_display_names()
        => Assert.Equal([new ModelInfo("claude-x", "Claude X")],
            ModelListParsers.Anthropic("""{"data":[{"id":"claude-x","display_name":"Claude X","type":"model"}],"has_more":false}"""));

    [Fact]
    public void Malformed_json_is_a_model_list_error()
        => Assert.Throws<ModelListException>(() => ModelListParsers.OpenAi("<html>not json</html>"));
}
