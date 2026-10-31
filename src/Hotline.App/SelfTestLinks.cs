using Hotline.App.Chat;
using Hotline.App.Interop;
using Hotline.App.Settings;
using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Tools;

namespace Hotline.App;

/// <summary>
/// hotline://selftest: headless checks of the real app, nothing shown to the user. Always: the off-screen transcript,
/// OCR and "/" checks, and every settings page. Optional (query): ?voice checks dictation can be set up (never opens the
/// microphone); ?selection reads the foreground app's selection via UI Automation (logs its length only) and ?tools starts the
/// mcp.json servers, and those two only run when ~/.hotline/.allow-selftest-extras exists (any web page can open a hotline:// link;
/// see <see cref="SelfTestOptions"/>).
/// </summary>
internal sealed class SelfTestLinks(ChatPresenter presenter, Func<SettingsHost?> settingsHost, Func<McpToolHost?> tools, Func<string?> odrPath,
    string dataDirectory, FileLog log)
{
    public void Run(Uri? uri)
    {
        var (allowed, refused) = SelfTestOptions.Resolve(uri, File.Exists(SelfTestOptions.MarkerPath(dataDirectory)));
        foreach (var option in refused) // names only, never a value
            log.Info($"selftest ?{LogText.Clean(option)} refused: it needs the file {SelfTestOptions.MarkerFile} in the Hotline data folder");
        presenter.SelfTest(uri);
        var problems = settingsHost()?.SelfTest() ?? [];
        if (problems.Count == 0) log.Info("selftest settings ok");
        else log.Error("selftest settings FAILED: " + string.Join("; ", problems));
        _ = LogThisPcAsync();
        if (allowed.Contains("selection")) CheckSelection();
        if (allowed.Contains("voice")) _ = CheckVoiceAsync();
        if (allowed.Contains("tools") && tools() is { } host) _ = Task.Run(() => CheckToolsAsync(host));
    }

    private async Task LogThisPcAsync()
    {
        try
        {
            foreach (var f in await ThisPc.CheckAsync(odrPath()))
                log.Info($"selftest this PC: {f.Name}: {(f.Available ? "yes" : "no")} ({f.Detail})");
        }
        catch (Exception ex) { log.Error("selftest this PC FAILED", ex); }
    }

    private void CheckSelection()
    {
        var fg = Native.GetForegroundWindow();
        var read = SelectionReader.Read(fg, AttachSelectionMode.Auto, log); // never Ctrl+C here
        log.Info($"selftest selection: {(read is null ? "none" : $"{read.Text.Length} chars")} ({Native.ClassNameOf(fg)})");
    }

    private async Task CheckVoiceAsync()
    {
        try { log.Info($"selftest voice: {await presenter.VoiceSelfTestAsync()}"); }
        catch (Exception ex) { log.Error("selftest voice FAILED", ex); }
    }

    private async Task CheckToolsAsync(McpToolHost host)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var list = await host.GetToolsAsync(timeout.Token);
            log.Info($"selftest tools: {list.Count} tool(s); " + string.Join(", ", host.Status.Select(s => $"{s.Name}={s.State}")));
        }
        catch (Exception ex) { log.Error("selftest tools FAILED", ex); }
    }
}
