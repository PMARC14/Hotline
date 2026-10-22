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

    public static async Task DisableAsync()
    {
        try { (await StartupTask.GetAsync(TaskId)).Disable(); }
        catch (Exception) { } // unpackaged dev run
    }

    public static string Describe(StartupTaskState? state) => state switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => Strings.Startup_On,
        StartupTaskState.DisabledByUser => Strings.Startup_OffByUser,
        StartupTaskState.DisabledByPolicy => Strings.Startup_OffByPolicy,
        StartupTaskState.Disabled => Strings.Startup_Off,
        _ => Strings.Startup_Unavailable,
    };
}
