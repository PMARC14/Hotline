using Windows.ApplicationModel;

namespace Hotline.App;

/// <summary>
/// "Start with Windows" = the manifest's StartupTask (HotlineStartup). Hotline must run after a reboot (e.g. Windows
/// Update restarts overnight) or the Copilot key cold-starts it. Windows lets an app enable the task only when the
/// user hasn't switched it off; otherwise the user re-enables it in Settings › Apps › Startup.
/// </summary>
internal static class StartupRegistration
{
    private const string TaskId = "HotlineStartup";

    public static async Task<StartupTaskState?> GetStateAsync()
    {
        try { return (await StartupTask.GetAsync(TaskId)).State; }
        catch (Exception) { return null; } // unpackaged dev run
    }

    /// <summary>Tries to enable; returns the resulting state.</summary>
    public static async Task<StartupTaskState?> EnableAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return task.State == StartupTaskState.Disabled ? await task.RequestEnableAsync() : task.State;
        }
        catch (Exception) { return null; }
    }

    public static void Disable()
    {
        try { StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult().Disable(); }
        catch (Exception) { }
    }

    public static string Describe(StartupTaskState? state) => state switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => "On — Hotline starts in the tray when you sign in.",
        StartupTaskState.DisabledByUser => "Off (turned off in Windows Settings › Apps › Startup). Turn Hotline on there.",
        StartupTaskState.DisabledByPolicy => "Off (blocked by your organization's policy).",
        StartupTaskState.Disabled => "Off.",
        _ => "Not available in this build.",
    };
}
