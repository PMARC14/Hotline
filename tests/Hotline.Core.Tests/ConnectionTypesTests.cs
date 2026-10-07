using Hotline.Core.Backends;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class ConnectionTypesTests
{
    [Fact]
    public void Every_backend_type_has_exactly_one_entry()
        => Assert.Equal(Enum.GetValues<BackendType>().Order(), ConnectionTypes.All.Select(t => t.Type).Order());

    [Fact]
    public void Only_verified_effort_levels_are_offered()
    {
        Assert.Equal(["low", "medium", "high"], ConnectionTypes.Of(BackendType.Antigravity).EffortLevels);
        Assert.True(ConnectionTypes.Of(BackendType.Antigravity).EffortInModelId);
        Assert.Equal(["low", "medium", "high", "xhigh", "max"], ConnectionTypes.Of(BackendType.ClaudeCode).EffortLevels);
        Assert.False(ConnectionTypes.Of(BackendType.ClaudeCode).EffortInModelId);
        Assert.Equal(["low", "medium", "high", "xhigh", "max"], ConnectionTypes.Of(BackendType.Anthropic).EffortLevels);
        Assert.Equal(["low", "medium", "high"], ConnectionTypes.Of(BackendType.Gemini).EffortLevels);
        Assert.Equal(["low", "medium", "high"], ConnectionTypes.Of(BackendType.OpenAiCompatible).EffortLevels);
        Assert.Empty(ConnectionTypes.Of(BackendType.Local).EffortLevels);
    }

    [Fact]
    public void Api_types_need_endpoint_and_key_cli_types_need_cli_path_and_tools()
    {
        foreach (var t in new[] { BackendType.Gemini, BackendType.Anthropic, BackendType.OpenAiCompatible, BackendType.Local })
        {
            var info = ConnectionTypes.Of(t);
            Assert.True(info.Has(ConnectionField.Endpoint) && info.Has(ConnectionField.ApiKey));
            Assert.False(string.IsNullOrEmpty(info.DefaultEndpoint));
        }
        foreach (var t in new[] { BackendType.Antigravity, BackendType.ClaudeCode })
            Assert.True(ConnectionTypes.Of(t).Has(ConnectionField.CliPath) && ConnectionTypes.Of(t).Has(ConnectionField.Tools));
        Assert.True(ConnectionTypes.Of(BackendType.Local).ApiKeyOptional);
    }
}
