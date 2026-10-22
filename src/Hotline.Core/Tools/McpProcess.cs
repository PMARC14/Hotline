using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Tools;

/// <summary>
/// How an MCP server process is started. Like the SDK's StdioClientTransport (2.2.0) so configs behave the same, except
/// that on Windows only what isn't an .exe (<c>npx</c>/<c>.cmd</c> shims, a command not found) runs through
/// <c>cmd.exe /c</c>, and then only with arguments cmd passes as they are: cmd expands <c>%VAR%</c> even inside quotes
/// and doesn't treat <c>\"</c> as an escape, so a quote, <c>%</c>, <c>&amp;</c> and the like in mcp.json (or a
/// discovered server) could run extra commands. The environment is inherited with the server's entries on top.
/// We start the process ourselves so the app can put it in its kill-on-close job.
/// </summary>
public static partial class McpProcess
{
    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <param name="fileExists">Looks up the command the way cmd would (tests pass a fake file system).</param>
    /// <exception cref="IOException">The server would need cmd.exe and has a character it can't pass safely.</exception>
    public static ProcessStartInfo CreateStartInfo(McpServerConfig server, bool windows, Func<string, bool>? fileExists = null)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = server.WorkingDirectory ?? Environment.CurrentDirectory,
            StandardInputEncoding = NoBomUtf8,
            StandardOutputEncoding = NoBomUtf8,
            StandardErrorEncoding = NoBomUtf8,
        };
        foreach (var (k, v) in server.Env) psi.Environment[k] = v;
        var command = server.Command;
        IEnumerable<string> args = server.Args;
        // An explicit cmd.exe is the user's own command line, passed as written.
        if (windows && !string.Equals(Path.GetFileName(command), "cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = Platform.CommandResolver.Find(command, server.WorkingDirectory, psi.Environment.TryGetValue("PATH", out var path) ? path : null,
                psi.Environment.TryGetValue("PATHEXT", out var ext) ? ext : null, fileExists ?? File.Exists);
            if (resolved is not null && Path.GetExtension(resolved).ToLowerInvariant() is ".exe" or ".com")
                command = resolved; // started directly: arguments reach it exactly as written
            else
            {
                if (server.Args.Prepend(command).Any(CmdUnsafe().IsMatch))
                    throw new IOException(L.F(Strings.Mcp_UnsafeArgument, server.Name, command));
                args = ["/c", command, .. server.Args];
                command = "cmd.exe";
            }
        }
        psi.FileName = command;
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    [GeneratedRegex("[\"%^&|<>!\r\n]", RegexOptions.CultureInvariant)]
    private static partial Regex CmdUnsafe();
}
