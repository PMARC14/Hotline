using System.Diagnostics;
using System.Text;
using Hotline.Core.Processes;
using Hotline.Core.Text.Localization;

namespace Hotline.App.Chat;

internal static class CliRunner
{
    /// <summary>Runs a command-line tool and returns its standard output (killed after <paramref name="timeout"/>).</summary>
    /// <param name="adopt">Puts the process in Hotline's kill-on-close job so it can't outlive the app.</param>
    public static async Task<string> RunAsync(string exe, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct, Action<Process>? adopt = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Hotline.Core.Processes.ProcessLaunches.Noted();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
        adopt?.Invoke(process);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        // Output is untrusted: past the cap, stop reading both streams and the program.
        async Task<string> Read(StreamReader reader)
        {
            try { return await BoundedLineReader.ReadToEndAsync(reader, MaxOutputChars, cts.Token); }
            catch (OutputTooLongException) { cts.Cancel(); throw; }
        }
        var stdout = Read(process.StandardOutput);
        var stderr = Read(process.StandardError);
        try
        {
            await Task.WhenAll(stdout, stderr);
            await process.WaitForExitAsync(cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or OutputTooLongException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { /* already gone */ }
            if (ex is OutputTooLongException)
                throw new Hotline.Core.Backends.ModelListException(L.F(Strings.Cli_OutputTooLong, Path.GetFileNameWithoutExtension(exe)));
            throw;
        }
        if (process.ExitCode != 0)
            throw new Hotline.Core.Backends.ModelListException($"{Path.GetFileName(exe)} failed (exit {process.ExitCode}): {(await stderr).Trim()}");
        return await stdout;
    }

    /// <summary>Most output read from a tool (model lists, connector lists are a few KB).</summary>
    private const int MaxOutputChars = 1024 * 1024;

    /// <summary>
    /// Opens one of Hotline's own text files (.md/.json/.txt under its data folder or agy's settings folder) in its
    /// associated editor, Notepad if it has none. Anything else is ignored.
    /// </summary>
    public static void OpenInEditor(string path)
    {
        if (ShellOpen.EditableFile(path, Roots, File.Exists) is not { } file) return;
        try
        {
            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            { Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"), file); }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* nothing to open it with */ }
    }

    /// <summary>Opens (creating it) one of Hotline's own folders in Explorer. Anything else is ignored.</summary>
    public static void OpenFolder(string path)
    {
        if (ShellOpen.OpenableFolder(path, Roots) is not { } folder) return;
        try
        {
            Directory.CreateDirectory(folder);
            // Always quoted (ArgumentList quotes only paths with spaces): Explorer splits an unquoted path at commas.
            // OpenableFolder has no '"' and no trailing '\', so the quotes can't be broken out of.
            var psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
            { UseShellExecute = false, Arguments = "\"" + folder + "\"" };
            Hotline.Core.Processes.ProcessLaunches.Noted();
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    /// <summary>A Windows program by full path with one argument, passed as is (no shell, no switches from a name).</summary>
    private static void Start(string exe, string argument)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.ArgumentList.Add(argument);
        Hotline.Core.Processes.ProcessLaunches.Noted();
        Process.Start(psi)?.Dispose();
    }

    /// <summary>Folders whose files Hotline opens: its data folder (and the pre-1.0 one), agy's settings folder.</summary>
    private static IReadOnlyList<string> Roots
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return [App.DataDirectory, App.PrivateDirectory, Path.GetDirectoryName(Hotline.Core.Backends.Agy.AgyPermissions.SettingsPath(home))!];
        }
    }
}
