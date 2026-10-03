using Hotline.Core.Diagnostics;

namespace Hotline.App.Settings;

/// <summary>Opens the settings window (one at a time) and brings it to the front.</summary>
internal sealed class SettingsHost(Func<SettingsWindow> create, FileLog log)
{
    private SettingsWindow? _window;

    /// <summary>Self-test: create the window hidden, build all pages, close it. Returns problems (empty = fine).</summary>
    public IReadOnlyList<string> SelfTest()
    {
        if (_window is not null) return []; // the user has it open: don't touch it
        try
        {
            var window = create();
            var failures = window.BuildAllPages();
            window.Close();
            return failures;
        }
        catch (Exception ex) { return [$"window: {ex.Message}"]; }
    }

    public void Show()
    {
        try
        {
            if (_window is null)
            {
                _window = create();
                _window.Closed += (_, _) => _window = null;
                log.Info("settings window opened");
            }
            _window.Activate();
        }
        catch (Exception ex)
        {
            log.Error("settings window failed to open", ex);
            _window = null;
        }
    }
}
