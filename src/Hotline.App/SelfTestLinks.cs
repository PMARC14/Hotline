using Hotline.App.Chat;
using Hotline.App.Interop;
using Hotline.App.Settings;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Tools;

namespace Hotline.App;

/// <summary>
/// hotline://selftest: headless checks of the real app, nothing shown to the user. Always: the off-screen transcript,
/// OCR and "/" checks, and every settings page. Optional (query): ?selection reads the foreground app's selection via
/// UI Automation (logs its length only), ?voice checks dictation can be set up (never opens the microphone),
/// ?tools starts the mcp.json servers.
/// </summary>
internal sealed class SelfTestLinks(ChatPresenter presenter, Func<SettingsHost?> settingsHost, Func<McpToolHost?> tools, Func<string?> odrPath, FileLog log)
{
    public void Run(Uri? uri)
    {
        presenter.SelfTest(uri);
        var problems = settingsHost()?.SelfTest() ?? [];
        if (problems.Count == 0) log.Info("selftest settings ok");
        else log.Error("selftest settings FAILED: " + string.Join("; ", problems));
        _ = LogThisPcAsync();
        if (Has(uri, "selection")) CheckSelection();
        if (Has(uri, "voice")) _ = CheckVoiceAsync();
        if (Has(uri, "tools") && tools() is { } host) _ = Task.Run(() => CheckToolsAsync(host));
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

    private static bool Has(Uri? uri, string option) => uri?.Query.Contains(option, StringComparison.OrdinalIgnoreCase) == true;

    private void CheckSelection()
    {
        var fg = Native.GetForegroundWindow();
        var read = SelectionReader.Read(fg, AttachSelectionMode.Auto, log); // never Ctrl+C here
        log.Info($"selftest selection: {(read is null ? "none" : $"{read.Text.Length} chars")} ({Native.ClassNameOf(fg)})");
    }

    private async Task CheckVoiceAsync() => log.Info($"selftest voice: {await presenter.VoiceSelfTestAsync()}");

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
