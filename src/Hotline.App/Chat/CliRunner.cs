using System.Diagnostics;
using System.Text;

namespace Hotline.App.Chat;

internal static class CliRunner
{
    /// <summary>Runs a command-line tool and returns its standard output (killed after <paramref name="timeout"/>).</summary>
    public static async Task<string> RunAsync(string exe, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        _ = process.StandardError.ReadToEndAsync(cts.Token);
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw; }
        return await stdout;
    }

    /// <summary>Opens a file in its associated editor (Notepad if .md has no association).</summary>
    public static void OpenInEditor(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true }); }
    }

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
