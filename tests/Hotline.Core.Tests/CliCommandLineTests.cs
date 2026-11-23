using Hotline.Core.Backends;
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public class CliCommandLineTests
{
    [Fact]
    public void Command_line_at_or_below_limit_fits()
    {
        // Total = exe (10) + space (1) + arg (32,755) + NUL (1) = 32,767
        var exe = @"C:\app.exe"; // 10 chars
        var arg = new string('a', 32767 - 10 - 1 - 1);
        Assert.Equal(32767, CliCommandLine.ComputeCommandLineLength(exe, [arg]));
        CliCommandLine.RequireFits(exe, [arg], "TestProfile");
    }

    [Fact]
    public void Command_line_exceeding_limit_throws_not_configured_with_profile_name()
    {
        // Total = 32,768
        var exe = @"C:\app.exe";
        var arg = new string('a', 32768 - 10 - 1 - 1);
        Assert.Equal(32768, CliCommandLine.ComputeCommandLineLength(exe, [arg]));
        var ex = Assert.Throws<BackendException>(() => CliCommandLine.RequireFits(exe, [arg], "MyConnection"));
        Assert.Equal(BackendErrorKind.NotConfigured, ex.Kind);
        Assert.Contains("MyConnection", ex.Message);
    }

    [Fact]
    public void Escapes_arguments_and_computes_length_accurately()
    {
        var exe = @"C:\Program Files\Codex\codex.exe";
        var args = new[] { "-c", "model_verbosity=\"low\"", @"C:\path with spaces\" };
        var formatted = CliCommandLine.FormatCommandLine(exe, args);
        Assert.Equal(@"""C:\Program Files\Codex\codex.exe"" -c ""model_verbosity=\""low\"""" ""C:\path with spaces\\""", formatted);
        Assert.Equal(formatted.Length + 1, CliCommandLine.ComputeCommandLineLength(exe, args));
    }
}
