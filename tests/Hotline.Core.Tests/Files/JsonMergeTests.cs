using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Files;

namespace Hotline.Core.Tests.Files;

public sealed class JsonMergeTests
{
    [Theory]
    [InlineData("{\"future\":7,\"x\":1}", "{\"x\":1,\"default\":2}", "{\"x\":1,\"default\":2}", "{\"future\":7,\"x\":1}")]
    [InlineData("{\"a\":0,\"future\":7,\"b\":2}", "{\"a\":0,\"b\":2}", "{\"a\":1,\"b\":2}", "{\"a\":1,\"future\":7,\"b\":2}")]
    [InlineData("{\"o\":{\"future\":7,\"x\":1}}", "{\"o\":{\"x\":1}}", "{\"o\":{\"x\":2}}", "{\"o\":{\"future\":7,\"x\":2}}")]
    [InlineData("{\"o\":{\"future\":{\"a\":1},\"deep\":{\"unknown\":8,\"x\":1}}}", "{\"o\":{\"deep\":{\"x\":1}}}", "{\"o\":{\"deep\":{\"x\":2}}}", "{\"o\":{\"future\":{\"a\":1},\"deep\":{\"unknown\":8,\"x\":2}}}")]
    [InlineData("{}", "{\"x\":1}", "{\"x\":null}", "{}")]
    [InlineData("{\"x\":1,\"future\":7}", "{\"x\":1}", "{\"x\":null}", "{\"x\":null,\"future\":7}")]
    [InlineData("{\"x\":1}", "{\"x\":1}", "{}", "{\"x\":null}")]
    [InlineData("{}", "{\"o\":{\"x\":1,\"default\":2}}", "{\"o\":{\"x\":3,\"default\":2}}", "{\"o\":{\"x\":3}}")]
    [InlineData("{\"o\":null}", "{\"o\":{\"x\":1}}", "{\"o\":{\"x\":2}}", "{\"o\":{\"x\":2}}")]
    [InlineData("{\"o\":{\"future\":7}}", "{\"o\":null}", "{\"o\":{\"x\":2}}", "{\"o\":{\"future\":7,\"x\":2}}")]
    [InlineData("{\"a\":[{\"x\":1,\"future\":7}]}", "{\"a\":[{\"x\":1}]}", "{\"a\":[{\"x\":2}]}", "{\"a\":[{\"x\":2}]}")]
    [InlineData("{\"a\":[{\"x\":1,\"future\":7}]}", "{\"a\":[{\"x\":1}]}", "{\"a\":[{\"x\":1}]}", "{\"a\":[{\"x\":1,\"future\":7}]}")]
    [InlineData("{\"o\":{\"x\":1,\"future\":7}}", "{\"o\":{\"x\":1}}", "{\"o\":null}", "{\"o\":null}")]
    [InlineData("{}", "{\"o\":null}", "{\"o\":{\"x\":null}}", "{\"o\":{}}")]
    [InlineData("{\"x\":\"invalid\",\"future\":7}", "{\"x\":1}", "{\"x\":1}", "{\"x\":\"invalid\",\"future\":7}")]
    public void Applies_only_model_changes_in_place(string source, string before, string after, string expected)
    {
        var tree = JsonNode.Parse(source)!.AsObject();
        var oldModel = JsonNode.Parse(before)!.AsObject();
        var newModel = JsonNode.Parse(after)!.AsObject();
        var result = JsonMerge.Apply(tree, oldModel, newModel);
        Assert.Same(tree, result);
        Assert.Equal(expected, result.ToJsonString());
        Assert.Equal(before, oldModel.ToJsonString());
        Assert.Equal(after, newModel.ToJsonString());
    }

    [Fact]
    public void Typed_models_use_the_callers_serialization_options()
    {
        var tree = JsonNode.Parse("{\"unknown\":9,\"value\":1}")!.AsObject();
        var result = JsonMerge.Apply(tree, new { Value = 1 }, new { Value = 2 }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("{\"unknown\":9,\"value\":2}", result.ToJsonString());
    }
}
