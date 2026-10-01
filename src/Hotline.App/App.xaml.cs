using System.Diagnostics;
using Hotline.App.Chat;
using Hotline.App.Interop;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Processes;
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
    private ChatPresenter? _presenter;
    private BackendCache? _backends;

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

    /// <summary>Package-private folder (agy workspace); falls back for unpackaged dev runs.</summary>
    internal static string PrivateDirectory
    {
        get
        {
            try { return Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
            catch (Exception) // no package identity: running unpackaged (dev smoke run)
            { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hotline"); }
        }
    }

    /// <summary>User-visible data folder: %USERPROFILE%\.hotline (settings.json, logs, history).</summary>
    internal static string DataDirectory => HotlinePaths.DataDirectory(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dataDir = DataDirectory;
        var privateDir = PrivateDirectory;
        bool migrated;
        try { migrated = HotlinePaths.MigrateFromLegacy(privateDir, dataDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { migrated = false; Debug.WriteLine(ex); }
        _log = new FileLog(Path.Combine(dataDir, "logs", "hotline.log"));
        var store = new SettingsStore(dataDir);
        var settings = store.Load();
        _log.Verbose = IsDebugBuild || settings.Diagnostics.VerboseLogging;
        _log.Info($"starting {(IsDebugBuild ? "DEBUG" : "release")} build {typeof(App).Assembly.GetName().Version}; verbose={_log.Verbose}; settings at {store.FilePath}");
        if (migrated) _log.Info($"migrated settings and history from {privateDir} to {dataDir}");

        _popup = new PopupWindow(settings.Window, settings.Chat.GrowMode, new PopupToggleGuard(TimeProvider.System, TimeSpan.FromMilliseconds(300)), _log, showDebugStatus: _log.Verbose);
        _router = new ActivationRouter(_popup, settings.Activation,
            new KeyEventDeduper(TimeProvider.System, TimeSpan.FromMilliseconds(1000)), _log);
        var job = new ChildProcessJob(_log);
        var agyWorkspace = new AgyWorkspace(Path.Combine(privateDir, "agy-workspace"), TimeProvider.System);
        try { agyWorkspace.PruneAttachments(TimeSpan.FromDays(1)); } catch (IOException ex) { _log.Error("attachment prune failed", ex); }
        var deps = new BackendDeps(new SystemLineProcessFactory(job.Add), agyWorkspace, _log, File.Exists,
            Environment.GetEnvironmentVariable("LOCALAPPDATA"), Environment.GetEnvironmentVariable("PATH"));
        _backends = new BackendCache(() => settings.Chat.Backends, p => BackendFactory.Create(p, deps));
        HistoryStore? history = null;
        if (settings.Chat.SaveHistory)
        {
            history = new HistoryStore(Path.Combine(dataDir, "history"), TimeProvider.System);
            try { history.Prune(settings.Chat.HistoryRetentionDays); } catch (IOException ex) { _log.Error("history prune failed", ex); }
        }
        var chat = new ChatController(_backends.Get, history, TimeProvider.System, _log) { BackendId = settings.Chat.DefaultBackend };
        _presenter = new ChatPresenter(_popup, chat, new AttachmentTray(new AttachmentLimits()), settings, store, _log, () => settings.Chat.Backends, dataDir);
        try { _presenter.Initialize(); }
        catch (Exception ex) { _log.Error("chat panel failed to initialize", ex); }

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
            onQuit: () => { _tray?.Dispose(); Task.Run(async () => { if (_backends is not null) await _backends.DisposeAllAsync(); }).Wait(TimeSpan.FromSeconds(2)); Exit(); });

        try { _router.OnActivation(ActivationRouter.Snapshot(_initialActivation, isFirstLaunch: true)); }
        catch (Exception ex) { _log.Error("initial activation handling failed", ex); }
    }
}
