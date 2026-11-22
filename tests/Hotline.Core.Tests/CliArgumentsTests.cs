using Hotline.Core.Backends;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Backends.ClaudeCode;
using Hotline.Core.Backends.Codex;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class CliArgumentsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(941)]
    public void Join_split_round_trips_generated_string_lists(int seed)
    {
        string[] edgeCases = ["--foo=bar\"baz", "--verbose", "", "\"", "\\", "x\\\\", "a\\\"b", "x y\\", "a\"b\"\\", "\t\r\n", "雪"];
        Assert.Equal(edgeCases, CliArguments.Split(CliArguments.Join(edgeCases)));
        var random = new Random(seed);
        const string alphabet = "ab= -\"\\\t\r\n雪\0";
        for (var sample = 0; sample < 1000; sample++)
        {
            var args = Enumerable.Range(0, random.Next(9)).Select(_ =>
                new string(Enumerable.Range(0, random.Next(64)).Select(_ => random.Next(4) == 0
                    ? (char)random.Next(char.MaxValue + 1) : alphabet[random.Next(alphabet.Length)]).ToArray())).ToArray();
            Assert.Equal(args, CliArguments.Split(CliArguments.Join(args)));
        }
    }

    [Fact]
    public void Quoted_paths_round_trip_and_permission_filters_still_apply()
    {
        // 1. Quoted paths, empty arguments, tabs, embedded TOML strings
        var input = "--mcp-config\t\"C:\\My Files\\servers.json\"\t-c model_verbosity=\"low\" --empty \"\" --trailing \"C:\\Program Files\\\\\"";
        var tokens = CliArguments.Split(input);
        Assert.Equal(["--mcp-config", @"C:\My Files\servers.json", "-c", "model_verbosity=\"low\"", "--empty", "", "--trailing", @"C:\Program Files\"], tokens);

        // Reversible Join -> Split
        var joined = CliArguments.Join(tokens);
        var reparsed = CliArguments.Split(joined);
        Assert.Equal(tokens, reparsed);

        // 2. Dangerous-flag cases across the three protocols
        // ClaudeCodeProtocol: dangerous skip permissions and blocked flags
        var claudeSafe = ClaudeCodeProtocol.BuildArgs(new ConnectionProfile
        {
            Tools = ToolMode.ChatOnly,
            ExtraArgs = Hotline.Core.Backends.CliArguments.SplitExtra("--allowedTools Bash Edit --dangerously-skip-permissions --mcp-config \"C:\\My Files\\m.json\" --verbose").ToList()
        }, "prompt");
        Assert.DoesNotContain(claudeSafe, a => a.Contains("dangerous", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Bash", claudeSafe);
        Assert.DoesNotContain("Edit", claudeSafe);
        Assert.DoesNotContain(@"C:\My Files\m.json", claudeSafe);
        Assert.Contains("--verbose", claudeSafe);

        // CodexProtocol: dangerous bypass and custom settings
        var codexSafe = CodexProtocol.BuildArgs(new ConnectionProfile
        {
            Tools = ToolMode.ChatOnly,
            ExtraArgs = Hotline.Core.Backends.CliArguments.SplitExtra("--dangerously-bypass-approvals-and-sandbox --sandbox danger-full-access -c model_verbosity=\"low\"").ToList()
        }, "prompt", null, []);
        Assert.DoesNotContain(codexSafe, a => a.Contains("dangerous", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("danger-full-access", codexSafe);
        Assert.Contains("-c", codexSafe);
        Assert.Contains("model_verbosity=\"low\"", codexSafe);

        // AgyProtocol: dangerous skip permissions and agent override
        var agySafe = AgyProtocol.BuildArgs(new ConnectionProfile
        {
            Tools = ToolMode.ChatOnly,
            ExtraArgs = Hotline.Core.Backends.CliArguments.SplitExtra("--dangerously-skip-permissions --agent malicious --model gemini-3.8-flash").ToList()
        });
        Assert.DoesNotContain(agySafe, a => a.Contains("dangerous", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("malicious", agySafe);
        Assert.Contains("gemini-3.8-flash", agySafe);
    }

    [Fact]
    public void Raw_codex_toml_quotes_are_preserved()
    {
        var tokens = CliArguments.SplitExtra("-c model_verbosity=\"low\"");
        Assert.Equal(["-c", "model_verbosity=\"low\""], tokens);

        var joined = CliArguments.Join(tokens);
        Assert.Equal("-c \"model_verbosity=\\\"low\\\"\"", joined);
        Assert.Equal(tokens, CliArguments.Split(joined));
    }

    [Fact]
    public void Quoted_path_with_spaces_is_one_argument()
    {
        var tokens = CliArguments.Split("--mcp-config \"C:\\My Files\\servers.json\"");
        Assert.Equal(["--mcp-config", @"C:\My Files\servers.json"], tokens);
    }

    [Fact]
    public void Saved_argument_arrays_round_trip()
    {
        string[] original = ["--mcp-config", @"C:\My Files\servers.json", "-c", "model_verbosity=\"low\"", ""];
        var joined = CliArguments.Join(original);
        var restored = CliArguments.Split(joined);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Trailing_backslash_and_malformed_quotes_handled()
    {
        // Trailing backslash in path with spaces
        string[] withSlash = [@"C:\Program Files\"];
        var joined = CliArguments.Join(withSlash);
        Assert.Equal(@"""C:\Program Files\\""", joined);
        Assert.Equal(withSlash, CliArguments.Split(joined));

        // Malformed / unclosed quote does not throw and recovers remaining text
        var malformed = CliArguments.Split("foo \"bar baz");
        Assert.Equal(["foo", "bar baz"], malformed);
    }
}
