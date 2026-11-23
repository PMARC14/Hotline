using Hotline.Core.Backends.ClaudeCode;
using Hotline.Core.Backends.Codex;

namespace Hotline.Core.Tests;

public sealed class CliPathSafetyTests
{
    /// <summary>A share or device path is refused before anything touches it (Windows would connect and send the user's hash).</summary>
    [Theory]
    [InlineData(@"\\server\share\claude.exe")]
    [InlineData(@"\\?\UNC\server\share\claude.exe")]
    [InlineData(@"//server/share/claude.exe")]
    public void Configured_cli_paths_on_a_share_are_never_probed(string path)
    {
        var probed = new List<string>();
        bool Exists(string p) { probed.Add(p); return true; }
        Assert.Null(ClaudeLocator.Find(path, Exists, null, null));
        Assert.Null(CodexLocator.Find(path, Exists, null, "x64"));
        Assert.Empty(probed);
    }
}
