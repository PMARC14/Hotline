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

    public App(AppActivationArguments initialActivation)
    {
        _initialActivation = initialActivation;
        InitializeComponent();
        UnhandledException += (_, e) => _log?.Error("unhandled exception", e.Exception);
    }

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
        _log.Info($"starting; settings at {store.FilePath}");

        _popup = new PopupWindow(settings.Window, new PopupToggleGuard(TimeProvider.System, TimeSpan.FromMilliseconds(300)));
        _router = new ActivationRouter(_popup, settings.Activation,
            new KeyEventDeduper(TimeProvider.System, TimeSpan.FromMilliseconds(150)), _log);

        AppInstance.GetCurrent().Activated += (_, a) =>
            _popup.DispatcherQueue.TryEnqueue(() => _router.OnActivation(a, isFirstLaunch: false));

        _router.OnActivation(_initialActivation, isFirstLaunch: true);
    }
}
