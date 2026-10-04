using System.Diagnostics;
using Hotline.App.Chat;
using Hotline.App.Interop;
using Hotline.App.Settings;
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
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _heartbeat;
    private ProviderBar? _providerBar;
    private SettingsService? _settingsService;
    private PromptLibrary? _prompts;
    private ModelCatalog? _models;
    private ISecretStore? _secrets;
    private readonly HashSet<string> _pendingInvalidations = [];
    private SettingsHost? _settingsHost;
    private Hotline.Core.Tools.McpToolHost? _toolHost;
    private IReadOnlyList<Hotline.Core.Tools.McpServerConfig> _odrServers = [];
    private string? _odrPath;
    private FileSystemWatcher? _settingsWatcher;

    public App(AppActivationArguments initialActivation)
    {
        _initialActivation = initialActivation;
        InitializeComponent();
        // Log and keep running: an exception in one button handler must not take the whole app down.
        UnhandledException += (_, e) => { _log?.Error("unhandled UI exception (kept running)", e.Exception); e.Handled = true; };
        // Last chance: record anything that is about to terminate the process.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _log?.Error($"FATAL (terminating={e.IsTerminating})", e.ExceptionObject as Exception);
        // Lifecycle breadcrumbs: Hotline once vanished during Modern Standby without a crash record.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _log?.Info($"process exiting (exit code {Environment.ExitCode})");
        Microsoft.Windows.System.Power.PowerManager.SystemSuspendStatusChanged += (_, _) =>
            _log?.Info($"system suspend status: {Microsoft.Windows.System.Power.PowerManager.SystemSuspendStatus}");
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
        bool migrated = false, migrationFailed = false;
        try { migrated = HotlinePaths.MigrateFromLegacy(privateDir, dataDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { migrationFailed = true; Debug.WriteLine(ex); }
        // If the move failed, keep using the old settings this session (never write defaults over the user data).
        if (migrationFailed) dataDir = privateDir;
        _log = new FileLog(Path.Combine(dataDir, "logs", "hotline.log"));
        var store = new SettingsStore(dataDir);
        var settings = store.Load();
        _log.Verbose = IsDebugBuild || settings.Diagnostics.VerboseLogging;
        _log.Info($"starting {(IsDebugBuild ? "DEBUG" : "release")} build {typeof(App).Assembly.GetName().Version}; verbose={_log.Verbose}; settings at {store.FilePath}");
        if (migrated) _log.Info($"migrated settings and history from {privateDir} to {dataDir}");
        _ = LogStartupStateAsync();
        _settingsService = new SettingsService(store, settings, _log);
        _prompts = new PromptLibrary(Path.Combine(dataDir, "prompts"));
        try { _prompts.EnsureDefault(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Error("prompt folder setup failed", ex); }
        _secrets = new PasswordVaultSecretStore(_log);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var prompts = _prompts;

        _popup = new PopupWindow(settings.Window, settings.Chat.GrowMode, new PopupToggleGuard(TimeProvider.System, TimeSpan.FromMilliseconds(300)), _log, showDebugStatus: _log.Verbose);
        _router = new ActivationRouter(_popup, settings.Activation,
            new KeyEventDeduper(TimeProvider.System, TimeSpan.FromMilliseconds(1000)), _log);
        var job = new ChildProcessJob(_log);
        var agyWorkspace = new AgyWorkspace(Path.Combine(privateDir, "agy-workspace"), TimeProvider.System);
        try { agyWorkspace.PruneAttachments(TimeSpan.FromDays(1)); } catch (IOException ex) { _log.Error("attachment prune failed", ex); }
        var deps = new BackendDeps(new SystemLineProcessFactory(job.Add), agyWorkspace, _log, File.Exists,
            Environment.GetEnvironmentVariable("LOCALAPPDATA"), Environment.GetEnvironmentVariable("PATH"),
            p => prompts.Read(p.Prompt ?? settings.Chat.DefaultPrompt), home, Path.Combine(privateDir, "claude-workspace"),
            _secrets, BackendFactory.CreateApiHttpClient(), ToolHost(dataDir));
        _backends = new BackendCache(() => settings.Chat.Backends, p => BackendFactory.Create(p, deps));
        HistoryStore? history = null;
        if (settings.Chat.SaveHistory)
        {
            history = new HistoryStore(Path.Combine(dataDir, "history"), TimeProvider.System);
            try { history.Prune(settings.Chat.HistoryRetentionDays); } catch (IOException ex) { _log.Error("history prune failed", ex); }
        }
        var chat = new ChatController(_backends.Get, history, TimeProvider.System, _log) { BackendId = settings.Chat.DefaultBackend };
        _presenter = new ChatPresenter(_popup, chat, new AttachmentTray(new AttachmentLimits()), settings, store, _log, dataDir);
        if (history is not null) _presenter.RecentChats = count => history.Recent(count);
        try { _presenter.Initialize(); }
        catch (Exception ex) { _log.Error("chat panel failed to initialize", ex); }

        _models = new ModelCatalog(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) }, _secrets, (p, ct) =>
        {
            var exe = AgyLocator.Find(p.CliPath, File.Exists, Environment.GetEnvironmentVariable("LOCALAPPDATA"), Environment.GetEnvironmentVariable("PATH"))
                      ?? throw new ModelListException("The Antigravity CLI (agy) isn't installed.");
            return CliRunner.RunAsync(exe, ["models"], TimeSpan.FromSeconds(30), ct, job.Add);
        }, TimeProvider.System);
        _providerBar = new ProviderBar(_popup, _settingsService, chat, InvalidateBackend, _models, _prompts, _presenter.Notice, _log);
        try { _providerBar.Initialize(); } catch (Exception ex) { _log.Error("provider bar failed to initialize", ex); }
        // Don't keep an idle CLI session (claude/agy) around after New chat or switching provider.
        var lastBackend = chat.BackendId;
        chat.Event += e => { if (e is ConversationReset) _ = InvalidateBackend(chat.BackendId).AsTask(); };
        _settingsService.Changed += () =>
        {
            if (chat.BackendId == lastBackend) return;
            _ = InvalidateBackend(lastBackend).AsTask();
            lastBackend = chat.BackendId;
        };
        _presenter.BusyChanged += busy =>
        {
            _providerBar.SetEnabled(!busy);
            if (!busy) _ = FlushInvalidationsAsync();
        };
        _router.SelfTestRequested += uri =>
        {
            _presenter.SelfTest(uri);
            var problems = _settingsHost?.SelfTest() ?? [];
            if (problems.Count == 0) _log.Info("selftest settings ok");
            else _log.Error("selftest settings FAILED: " + string.Join("; ", problems));
        };
        _router.DemoRequested += () => _presenter.Demo();
        _settingsHost = new SettingsHost(() => new SettingsWindow(_settingsService, _secrets, _models, InvalidateBackend,
            _prompts, store.FilePath, Path.Combine(dataDir, "logs"), _log, _toolHost, Path.Combine(dataDir, "mcp.json"), _odrPath), _log);
        _presenter.SettingsRequested += () => { _popup.HidePopup(); _settingsHost.Show(); };
        _router.OpenSettingsRequested += () => _settingsHost.Show();
        ApplyToolbarFile(dataDir);
        WatchSettingsFile(store);
        _settingsService.ConnectionsChanged += ids =>
        {
            foreach (var id in ids) _ = InvalidateBackend(id).AsTask();
            if (settings.Chat.Backends.All(b => b.Id != chat.BackendId)) chat.BackendId = settings.Chat.DefaultBackend;
        };
        _settingsService.Changed += () =>
        {
            try
            {
                _log.Verbose = IsDebugBuild || settings.Diagnostics.VerboseLogging;
                _popup.GrowMode = settings.Chat.GrowMode;
                _popup.ApplyAppearance();
                _presenter.ApplyAppearance();
                _providerBar.Refresh();
            }
            catch (Exception ex) { _log.Error("applying settings failed", ex); }
        };

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
            onOpenSettings: () => _settingsHost?.Show(),
            onRestart: () => { _tray?.Dispose(); StopToolServers(); AppInstance.Restart(string.Empty); },
            onQuit: () =>
            {
                _tray?.Dispose();
                // Both at once: a slow backend shutdown must not use up the time the MCP servers need (they're not in the job).
                try
                {
                    Task.Run(() => Task.WhenAll(
                        _toolHost is null ? Task.CompletedTask : _toolHost.DisposeAsync().AsTask(),
                        _backends is null ? Task.CompletedTask : _backends.DisposeAllAsync().AsTask())).Wait(TimeSpan.FromSeconds(3));
                }
                catch (Exception ex) { _log?.Error("shutdown cleanup failed", ex); } // still exit
                Exit();
            });

        _heartbeat = _popup.DispatcherQueue.CreateTimer();
        _heartbeat.Interval = TimeSpan.FromMinutes(30);
        _heartbeat.Tick += (_, _) => _log?.Info($"alive; working set {Environment.WorkingSet / (1024 * 1024)} MB");
        _heartbeat.Start();

        try { _router.OnActivation(ActivationRouter.Snapshot(_initialActivation, isFirstLaunch: true)); }
        catch (Exception ex) { _log.Error("initial activation handling failed", ex); }
    }

    /// <summary>
    /// Restarts a connection's backend after its settings change, but never mid-answer: while an answer streams the
    /// restart waits until it finishes (changes apply to the next message).
    /// </summary>
    private ValueTask InvalidateBackend(string id)
    {
        if (_presenter?.IsBusy == true) { _pendingInvalidations.Add(id); return ValueTask.CompletedTask; }
        return _backends?.InvalidateAsync(id) ?? ValueTask.CompletedTask;
    }

    private async Task FlushInvalidationsAsync()
    {
        foreach (var id in _pendingInvalidations.ToList())
        {
            _pendingInvalidations.Remove(id);
            try { if (_backends is not null) await _backends.InvalidateAsync(id); }
            catch (Exception ex) { _log?.Error($"restarting backend {id} failed", ex); }
        }
    }

    /// <summary>settings.json is the configuration: hand edits apply live (debounced; Hotline's own saves are no-ops).</summary>
    private void WatchSettingsFile(SettingsStore store)
    {
        var dir = Path.GetDirectoryName(store.FilePath)!;
        var timer = _popup!.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(400);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            try
            {
                ApplyToolbarFile(dir);
                if (store.TryRead() is not { } fromDisk) { _log?.Info("settings.json not readable yet (being edited?); keeping current settings"); return; }
                if (_settingsService!.Reload(fromDisk)) _log?.Info("settings.json changed on disk; applied");
            }
            catch (Exception ex) { _log?.Error("reloading settings failed", ex); }
        };
        try
        {
            // settings.json and connections\*.json (one file per connection): edits, new and deleted files apply live.
            _settingsWatcher = new FileSystemWatcher(dir, "*.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, IncludeSubdirectories = true };
            var toolbarPath = Path.Combine(dir, "toolbar.json");
            bool Relevant(string? path) => path is not null && (string.Equals(path, store.FilePath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, toolbarPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetDirectoryName(path), store.ConnectionsDirectory, StringComparison.OrdinalIgnoreCase));
            void Kick(string? path) { if (Relevant(path)) _popup.DispatcherQueue.TryEnqueue(() => { timer.Stop(); timer.Start(); }); }
            _settingsWatcher.Changed += (_, e) => Kick(e.FullPath);
            _settingsWatcher.Created += (_, e) => Kick(e.FullPath);
            _settingsWatcher.Deleted += (_, e) => Kick(e.FullPath);
            _settingsWatcher.Renamed += (_, e) => { Kick(e.FullPath); Kick(e.OldFullPath); };
            _settingsWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { _log?.Error("can't watch settings.json", ex); }
    }

    private async Task LogStartupStateAsync()
    {
        var state = await StartupRegistration.GetStateAsync();
        _log?.Info($"start with Windows: {state?.ToString() ?? "n/a"}");
    }

    /// <summary>
    /// MCP tools for API connections: servers from ~/.hotline/mcp.json plus the Windows on-device agent registry's
    /// connectors when odr.exe exists (discovered once in the background). Approvals go to the panel.
    /// </summary>
    private Hotline.Core.Tools.McpToolHost ToolHost(string dataDir)
    {
        var mcpPath = Path.Combine(dataDir, "mcp.json");
        _ = Hotline.Core.Tools.McpConfig.Load(mcpPath); // creates mcp.json with a commented example on first run
        _odrPath = Hotline.Core.Tools.OdrDiscovery.FindOdr(File.Exists, Environment.GetEnvironmentVariable("WINDIR"), Environment.GetEnvironmentVariable("LOCALAPPDATA"));
        _log?.Info(_odrPath is null ? "Windows agent registry: not available on this Windows" : $"Windows agent registry: {_odrPath}");
        if (_odrPath is { } odr)
            _ = Task.Run(async () =>
            {
                foreach (var args in new[] { new[] { "mcp", "list" }, new[] { "list" } })
                {
                    try
                    {
                        var json = await CliRunner.RunAsync(odr, args, TimeSpan.FromSeconds(20), CancellationToken.None);
                        var servers = Hotline.Core.Tools.OdrDiscovery.Parse(json);
                        if (servers.Count == 0) continue;
                        _odrServers = servers;
                        _log?.Info($"Windows agent registry: {servers.Count} connector(s)");
                        return;
                    }
                    catch (Exception ex) { _log?.Error($"odr {string.Join(' ', args)} failed", ex); }
                }
            });
        _toolHost = new Hotline.Core.Tools.McpToolHost(
            () => Hotline.Core.Tools.McpConfig.Load(mcpPath), () => _odrServers, Hotline.Core.Tools.StdioMcpSession.ConnectAsync,
            (request, ct) => _presenter?.AskToolApprovalAsync(request, ct) ?? Task.FromResult(Hotline.Core.Tools.ToolDecision.Deny),
            _log!, mcpPath);
        return _toolHost;
    }

    /// <summary>~/.hotline/toolbar.json → the bottom bar (written with the defaults on first run; applies live).</summary>
    private void ApplyToolbarFile(string dataDir)
    {
        var config = Hotline.Core.Windowing.ToolbarConfig.Load(Path.Combine(dataDir, "toolbar.json"));
        if (config.Error is { } error) { _log?.Error(error); if (_toolbarApplied) return; } // keep the current bar
        if (_popup is null || _popup.ToolbarItems.SequenceEqual(config.Items) && _toolbarApplied) return;
        _toolbarApplied = true;
        try { _popup.ApplyToolbar(config.Items); }
        catch (Exception ex) { _log?.Error("applying toolbar.json failed", ex); }
    }

    private bool _toolbarApplied;

    /// <summary>MCP servers are started by the MCP SDK (not in the kill-on-close job): stop them before a restart.</summary>
    private void StopToolServers()
    {
        try { if (_toolHost is { } host) Task.Run(async () => await host.DisposeAsync()).Wait(TimeSpan.FromSeconds(3)); }
        catch (Exception ex) { _log?.Error("stopping MCP servers failed", ex); }
    }
}
