using System.Diagnostics;
using Hotline.App.Interop;
using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Hotline.App;

public partial class App : Application
{
    private readonly AppActivationArguments _initialActivation;
    private FileLog? _log;
    private PopupWindow? _popup;
    private ActivationRouter? _router;
    private TrayIcon? _tray;

    public App(AppActivationArguments initialActivation)
    {
        _initialActivation = initialActivation;
        InitializeComponent();
        UnhandledException += (_, e) => _log?.Error("unhandled XAML exception", e.Exception);
        // Last chance: record anything that is about to terminate the process.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _log?.Error($"FATAL (terminating={e.IsTerminating})", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => _log?.Error("unobserved task exception", e.Exception);
    }

#if DEBUG
    internal const bool IsDebugBuild = true;
#else
    internal const bool IsDebugBuild = false;
#endif

    internal static string DataDirectory
    {
        get
        {
            try { return Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
            catch (Exception) // no package identity: running unpackaged (dev smoke run)
            { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hotline"); }
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dataDir = DataDirectory;
        _log = new FileLog(Path.Combine(dataDir, "logs", "hotline.log"));
        var store = new SettingsStore(dataDir);
        var settings = store.Load();
        _log.Verbose = IsDebugBuild || settings.Diagnostics.VerboseLogging;
        _log.Info($"starting {(IsDebugBuild ? "DEBUG" : "release")} build {typeof(App).Assembly.GetName().Version}; verbose={_log.Verbose}; settings at {store.FilePath}");

        _popup = new PopupWindow(settings.Window, new PopupToggleGuard(TimeProvider.System, TimeSpan.FromMilliseconds(300)), _log, showDebugStatus: _log.Verbose);
        _router = new ActivationRouter(_popup, settings.Activation,
            new KeyEventDeduper(TimeProvider.System, TimeSpan.FromMilliseconds(1000)), _log);

        AppInstance.GetCurrent().Activated += (_, a) =>
        {
            // Snapshot now, on the event thread, while the redirecting process is still alive.
            ActivationRequest request;
            try { request = ActivationRouter.Snapshot(a, isFirstLaunch: false); }
            catch (Exception ex) { _log.Error("could not read redirected activation", ex); return; }
            // Exceptions escaping a DispatcherQueue callback fail-fast the process, so contain them.
            _popup.DispatcherQueue.TryEnqueue(() =>
            {
                try { _router.OnActivation(request); }
                catch (Exception ex) { _log.Error("activation handling failed", ex); }
            });
        };

        var hook = new WindowMessageHook(_popup.Hwnd, _log);
        CopilotFastPath.Register(hook, e => _router.OnKey(e, KeySource.FastPath), _log);
        HotkeyRegistration.TryRegister(hook, settings.Activation.FallbackHotkey,
            () => _router.OnKey(KeyEvent.Tap, KeySource.Hotkey), _log);
        _tray = new TrayIcon(hook, Path.Combine(AppContext.BaseDirectory, "Assets", "Hotline.ico"),
            onToggle: _router.TogglePopup,
            onOpenSettings: () => Process.Start(new ProcessStartInfo(store.FilePath) { UseShellExecute = true }),
            onRestart: () => { _tray?.Dispose(); AppInstance.Restart(string.Empty); },
            onQuit: () => { _tray?.Dispose(); Exit(); });

        try { _router.OnActivation(ActivationRouter.Snapshot(_initialActivation, isFirstLaunch: true)); }
        catch (Exception ex) { _log.Error("initial activation handling failed", ex); }
    }
}
