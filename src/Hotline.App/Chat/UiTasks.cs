using Hotline.Core.Diagnostics;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Chat;

/// <summary>Runs UI work so a failure is logged and shown as a notice instead of escaping (and killing) the app.</summary>
internal sealed class UiTasks(FileLog log, NoticeArea notices)
{
    public void Run(string what, Func<Task> work) => _ = RunAsync(what, work);

    public async Task RunAsync(string what, Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex)
        {
            log.Error($"{what} failed", ex);
            notices.Show($"Couldn't {what}: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    public void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { log.Error($"{what} failed", ex); }
    }
}
