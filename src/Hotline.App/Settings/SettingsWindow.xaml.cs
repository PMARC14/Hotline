using Hotline.App.Chat;
using Hotline.App.Interop;
using Hotline.Core.Activation;
using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Hotline.App.Settings;

/// <summary>Native settings window. Pages are generated from <see cref="SettingsSchema"/>; connections and prompts are custom.</summary>
public sealed partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly ModelCatalog _models;
    private readonly Func<string, ValueTask> _invalidate;
    private readonly PromptLibrary _prompts;
    private readonly string _settingsFile;
    private readonly string _logsDir;
    private readonly FileLog _log;

    private Hotline.Core.Tools.McpToolHost? _toolHost;
    private string _mcpPath = "";
    private string? _odrPath;
    /// <summary>The width (DIP) the panel's button rows have, or would have when it opens (the button rows editor's "Move what doesn't fit"); null in tests.</summary>
    private readonly Func<double>? _barRoom;

    internal SettingsWindow(SettingsService settings, ISecretStore secrets, ModelCatalog models, Func<string, ValueTask> invalidate,
        PromptLibrary prompts, string settingsFile, string logsDir, FileLog log, Hotline.Core.Tools.McpToolHost? toolHost = null,
        string? mcpPath = null, string? odrPath = null, Func<double>? barRoom = null)
    {
        (_toolHost, _mcpPath, _odrPath, _barRoom) = (toolHost, mcpPath ?? "", odrPath, barRoom);
        (_settings, _secrets, _models, _invalidate, _prompts, _settingsFile, _logsDir, _log) =
            (settings, secrets, models, invalidate, prompts, settingsFile, logsDir, log);
        InitializeComponent();
        Title = AppTitleBar.Title = Strings.Settings_Title;
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets\\Hotline.ico");
        var dpi = Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1000 * scale), (int)(740 * scale)));
        ApplyTheme();
        _settings.Changed += OnSettingsChanged;
        SystemTheme.Changed += OnSettingsChanged;
        Closed += (_, _) => { _settings.Changed -= OnSettingsChanged; SystemTheme.Changed -= OnSettingsChanged; };
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    /// <summary>Self-test: builds every page once (hidden window) so a broken page fails the smoke test.</summary>
    internal IReadOnlyList<string> BuildAllPages()
    {
        var failures = new List<string>();
        foreach (var tag in Nav.MenuItems.OfType<NavigationViewItem>().Select(i => (string)i.Tag))
        {
            try { ShowPage(tag); }
            catch (Exception ex) { failures.Add($"{tag}: {ex.Message}"); }
        }
        return failures;
    }

    private void OnSettingsChanged() => DispatcherQueue.TryEnqueue(ApplyTheme);

    private void ApplyTheme()
    {
        Root.RequestedTheme = SystemTheme.Resolve(_settings.Current.Window.Theme);
        SystemTheme.ApplyTitleBar(AppWindow, _settings.Current.Window.Theme);
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        => ShowPage(args.SelectedItemContainer?.Tag as string ?? "General");

    private string _currentPage = "General";

    /// <summary>Selects a page by its navigation tag (ignored if there is none).</summary>
    internal void OpenPage(string tag)
    {
        if (Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag as string == tag) is not { } item) return;
        if (ReferenceEquals(Nav.SelectedItem, item)) ShowPage(tag); // no SelectionChanged when it's already selected
        else Nav.SelectedItem = item;
    }

    private void ShowPage(string tag)
    {
        _currentPage = tag;
        PageHost.Children.Clear();
        PageHost.Children.Add(new TextBlock
        {
            Text = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == tag).Content as string,
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"], Margin = new Thickness(0, 0, 0, 12),
        });
        switch (tag)
        {
            case "Connections": BuildConnectionsPage(); break;
            case "Prompts": BuildPromptsPage(); break;
            case "Tools": BuildToolsPage(); break;
            case "Actions": BuildActionsPage(); break;
            case "About": BuildAboutPage(); break;
            case "Guide": BuildGuidePage(); break;
            case "Backup": BuildBackupPage(); break;
            default:
                var page = Enum.Parse<SettingsPage>(tag);
                foreach (var item in SettingsSchema.Items.Where(i => i.Page == page)) PageHost.Children.Add(BuildItem(item));
                if (page == SettingsPage.Advanced) BuildAdvancedExtras();
                if (page == SettingsPage.General) { BuildCopilotKeyCard(); BuildStartupCard(); }
                if (page == SettingsPage.Appearance) { BuildWindowsOverrideNote(); BuildSoundsSection(); }
                if (page == SettingsPage.Window) BuildToolbarSection();
                if (page == SettingsPage.Chat) BuildMemoryCard();
                break;
        }
    }

    // ---- generic setting cards -------------------------------------------------------------

    private Border Card(string header, string? description, UIElement? control) =>
        CardWithStatus(header, description is null ? null : new TextBlock { Text = description }, control);

    /// <summary>A setting card whose description can change later (e.g. a status line).</summary>
    private Border CardWithStatus(string header, TextBlock? descriptionBlock, UIElement? control)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = header, TextWrapping = TextWrapping.Wrap });
        if (descriptionBlock is not null)
        {
            (descriptionBlock.FontSize, descriptionBlock.Opacity, descriptionBlock.TextWrapping) = (12, 0.7, TextWrapping.Wrap);
            text.Children.Add(descriptionBlock);
        }
        grid.Children.Add(text);
        if (control is FrameworkElement fe)
        {
            Grid.SetColumn(fe, 1);
            fe.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(fe);
        }
        return new Border { Child = grid, Style = (Style)Root.Resources["CardStyle"] };
    }

    private Border BuildItem(SettingItem item)
    {
        // Schema text is resource keys (SettingsSchema); look them up now, in the interface language.
        var header = L.Get(item.Header);
        var description = item.Description is null ? null : L.Get(item.Description);
        if (item.RequiresRestart) description = description is null ? Strings.Settings_AppliesAfterRestart : $"{description} {Strings.Settings_AppliesAfterRestart}";
        var s = _settings.Current;
        switch (item)
        {
            case ToggleItem t:
            {
                var toggle = new ToggleSwitch { IsOn = t.Get(s), OnContent = "", OffContent = "", MinWidth = 0 };
                toggle.Toggled += (_, _) =>
                {
                    _settings.Update(x => t.Set(x, toggle.IsOn));
                    if (t.RefreshPage) DispatcherQueue.TryEnqueue(() => ShowPage(_currentPage));
                };
                return Card(header, description, toggle);
            }
            case NumberItem n:
            {
                var box = new NumberBox
                {
                    Minimum = n.Min, Maximum = n.Max, SmallChange = n.Step, LargeChange = n.Step * 10, Value = n.Get(s), Width = 150,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden, ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
                };
                box.ValueChanged += (_, e) => { if (!double.IsNaN(e.NewValue)) _settings.Update(x => n.Set(x, e.NewValue)); };
                var label = n.Unit is null ? (UIElement)box : new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { box, new TextBlock { Text = L.Get(n.Unit), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 } },
                };
                return Card(header, description, label);
            }
            case ChoiceItem c:
            {
                var combo = new ComboBox { MinWidth = 200 };
                foreach (var o in c.Options) combo.Items.Add(new ComboBoxItem { Content = L.Get(o.Label), Tag = o.Value });
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == c.Get(s));
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not ComboBoxItem { Tag: string v } || v == c.Get(_settings.Current)) return;
                    _settings.Update(x => c.Set(x, v));
                    if (c.RefreshPage) DispatcherQueue.TryEnqueue(() => ShowPage(_currentPage));
                };
                return Card(header, description, combo);
            }
            case HotkeyItem hk:
                return Card(header, description, HotkeyBinder(hk));
            case TextItem tx:
            {
                var box = new TextBox { Text = tx.Get(s) ?? "", PlaceholderText = tx.Placeholder is null ? "" : L.Get(tx.Placeholder), Width = 240 };
                box.LostFocus += (_, _) =>
                {
                    if (tx.Validate is { } valid && !string.IsNullOrWhiteSpace(box.Text) && !valid(box.Text.Trim()))
                    {
                        box.Header = tx.Invalid is null ? Strings.Settings_InvalidValue : L.Get(tx.Invalid);
                        return;
                    }
                    box.Header = null;
                    if ((tx.Get(_settings.Current) ?? "") != box.Text.Trim()) _settings.Update(x => tx.Set(x, box.Text));
                };
                return Card(header, description, box);
            }
            default:
                return Card(header, description, null);
        }
    }

    /// <summary>
    /// A key binder: click, then press the shortcut (Esc cancels, Backspace clears). It saves the same text settings.json
    /// holds, and refuses a combination another app already holds.
    /// </summary>
    private Button HotkeyBinder(HotkeyItem item)
    {
        var button = new Button { MinWidth = 200, HorizontalContentAlignment = HorizontalAlignment.Center };
        var capturing = false;
        void ShowValue() { capturing = false; button.Content = item.Get(_settings.Current) is { Length: > 0 } v ? v : Strings.Hotkey_NotSet; }
        void Save(string? value) { _settings.Update(x => item.Set(x, value)); ShowValue(); }
        ShowValue();
        button.Click += (_, _) => { capturing = true; button.Content = Strings.Hotkey_Press; };
        button.LostFocus += (_, _) => { if (capturing) ShowValue(); };
        button.PreviewKeyDown += (_, e) =>
        {
            if (!capturing) return;
            e.Handled = true; // while binding, every key belongs to the binder (Space/Enter don't click it again)
            var held = HotkeyModifiers.None;
            if (Down(Windows.System.VirtualKey.Control)) held |= HotkeyModifiers.Control;
            if (Down(Windows.System.VirtualKey.Menu)) held |= HotkeyModifiers.Alt;
            if (Down(Windows.System.VirtualKey.Shift)) held |= HotkeyModifiers.Shift;
            if (Down(Windows.System.VirtualKey.LeftWindows) || Down(Windows.System.VirtualKey.RightWindows)) held |= HotkeyModifiers.Win;
            var key = e.Key != Windows.System.VirtualKey.None ? e.Key : e.OriginalKey; // some Alt combinations report Key as None
            var result = HotkeyCapture.Press(held, (uint)key);
            switch (result.Outcome)
            {
                case HotkeyCapture.Outcome.Waiting: return;
                case HotkeyCapture.Outcome.Cancel: ShowValue(); return;
                case HotkeyCapture.Outcome.Clear: Save(null); return;
                case HotkeyCapture.Outcome.NeedsModifier: button.Content = Strings.Hotkey_NeedsModifier; return;
                case HotkeyCapture.Outcome.Unsupported: button.Content = Strings.Hotkey_Unsupported; return;
            }
            // Hotline's own current hotkey is "taken" by Hotline itself; anything else must be free.
            var isCurrent = string.Equals(result.Text, item.Get(_settings.Current), StringComparison.OrdinalIgnoreCase);
            if (!isCurrent && Hotkey.TryParse(result.Text, out var hotkey)
                && !HotkeyRegistration.IsFree(WinRT.Interop.WindowNative.GetWindowHandle(this), hotkey))
            {
                button.Content = Strings.Hotkey_InUse;
                return;
            }
            Save(result.Text);
        };
        return button;

        static bool Down(Windows.System.VirtualKey key) =>
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    /// <summary>Says when Windows' contrast theme or transparency switch overrides the colours or backdrop chosen here.</summary>
    private void BuildWindowsOverrideNote()
    {
        var windows = SystemTheme.Colors;
        var message = windows.Contrast is not null ? Strings.Set_Override_Contrast : !windows.Transparency ? Strings.Set_Override_Transparency : null;
        if (message is null) return;
        PageHost.Children.Insert(1, new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational, Message = message, Margin = new Thickness(0, 0, 0, 8) });
    }

    /// <summary>Choosing Hotline for the Copilot key happens in Windows Settings; this explains it and opens the page.</summary>
    private void BuildCopilotKeyCard()
    {
        var open = new Button { Content = Strings.Set_CopilotKeyApp_Open };
        open.Click += (_, _) => _ = Chat.CopilotKeySettings.OpenAsync(_log);
        PageHost.Children.Add(Card(Strings.Set_CopilotKeyApp, Strings.Set_CopilotKeyApp_Desc, open));
    }

    /// <summary>Start with Windows (the package's StartupTask; not a settings.json value — Windows owns it).</summary>
    private void BuildStartupCard()
    {
        var status = new TextBlock();
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0 };
        var updating = true;
        async void Refresh()
        {
            var state = await StartupRegistration.GetStateAsync();
            updating = true;
            toggle.IsOn = state is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            toggle.IsEnabled = state is not (null or Windows.ApplicationModel.StartupTaskState.DisabledByPolicy or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy);
            status.Text = StartupRegistration.Describe(state);
            updating = false;
        }
        toggle.Toggled += async (_, _) =>
        {
            if (updating) return;
            if (toggle.IsOn)
            {
                var state = await StartupRegistration.EnableAsync();
                if (state == Windows.ApplicationModel.StartupTaskState.DisabledByUser)
                    await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps"));
            }
            else await StartupRegistration.DisableAsync();
            Refresh();
        };
        PageHost.Children.Add(CardWithStatus(Strings.Settings_Startup, status, toggle));
        Refresh();
    }

    /// <summary>MCP servers (mcp.json + Windows agent registry), their status and the approval rules.</summary>
    private void BuildToolsPage()
    {
        Button Link(string text, Action action) { var b = new Button { Content = text }; b.Click += (_, _) => action(); return b; }
        PageHost.Children.Add(new TextBlock
        {
            Text = Strings.Tools_Intro,
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8),
        });
        PageHost.Children.Add(Card(Strings.Tools_Servers, L.F(Strings.Tools_Servers_Desc, _mcpPath),
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { Link(Strings.Tools_EditMcp, () => CliRunner.OpenInEditor(_mcpPath)), Link(Strings.Common_OpenFolder, () => CliRunner.OpenFolder(Path.GetDirectoryName(_mcpPath)!)) },
            }));
        PageHost.Children.Add(Card(Strings.Tools_Odr,
            _odrPath is null ? Strings.Tools_Odr_Missing : L.F(Strings.Tools_Odr_Available, _odrPath),
            null));
        if (_toolHost is null) return;
        var list = new StackPanel { Spacing = 6 };
        void Fill()
        {
            list.Children.Clear();
            if (_toolHost.ConfigError is { } error)
                list.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Error, Message = error });
            var status = _toolHost.Status;
            if (status.Count == 0)
                list.Children.Add(new TextBlock { Text = Strings.Tools_NoneChecked, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            foreach (var s in status.OrderBy(s => s.Name))
                list.Children.Add(Card(s.DisplayName, s.State switch
                {
                    Hotline.Core.Tools.McpServerState.Ready => L.F(Strings.Tools_Ready, s.ToolCount),
                    Hotline.Core.Tools.McpServerState.Failed => L.F(Strings.Tools_Failed, s.Error),
                    Hotline.Core.Tools.McpServerState.Disabled => Strings.Tools_Disabled,
                    _ => s.State.ToString(),
                }, null));
        }
        var check = new Button { Content = Strings.Tools_Check, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // a server stuck starting can't hold the button forever
            try
            {
                _toolHost.RetryFailedServers();
                await _toolHost.GetToolsAsync(timeout.Token);
            }
            catch (Exception ex) { _log.Error("checking MCP servers failed", ex); }
            finally { check.IsEnabled = true; Fill(); }
        };
        PageHost.Children.Add(check);
        PageHost.Children.Add(list);
        Fill();
    }

    private void BuildAdvancedExtras()
    {
        Button Link(string text, Action action)
        {
            var b = new Button { Content = text };
            b.Click += (_, _) => action();
            return b;
        }
        var folder = Path.GetDirectoryName(_settingsFile)!;
        PageHost.Children.Add(Card(Strings.Advanced_SettingsFile, _settingsFile, new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { Link(Strings.Common_OpenFolder, () => CliRunner.OpenFolder(folder)), Link(Strings.Advanced_EditSettings, () => CliRunner.OpenInEditor(_settingsFile)) },
        }));
        var connections = Path.Combine(folder, Hotline.Core.Settings.SettingsStore.ConnectionsFolder);
        PageHost.Children.Add(Card(Strings.Nav_Connections, L.F(Strings.Advanced_Connections_Desc, connections),
            Link(Strings.Advanced_OpenConnections, () => CliRunner.OpenFolder(connections))));
        PageHost.Children.Add(Card(Strings.Advanced_Logs, _logsDir, Link(Strings.Advanced_OpenLogs, () => CliRunner.OpenFolder(_logsDir))));
        // Reset, with a backup first; switching between sets of settings is just replacing the files (applied live).
        async Task ResetAsync(bool everything)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = everything ? Strings.Advanced_ResetAllTitle : Strings.Advanced_ResetTitle,
                Content = new TextBlock { Text = everything ? Strings.Advanced_ResetAllBody : Strings.Advanced_ResetBody, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = Strings.Advanced_ResetConfirm, CloseButtonText = Strings.Common_Cancel, DefaultButton = ContentDialogButton.Close,
            };
            if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
            try
            {
                var (backup, removed) = _settings.Reset(everything);
                foreach (var id in removed)
                {
                    _secrets.Set(SecretKeys.ApiKey(id), null);
                    await _invalidate(id);
                }
                ShowPage("Advanced");
                PageHost.Children.Insert(0, new InfoBar { IsOpen = true, Severity = InfoBarSeverity.Success, Message = L.F(Strings.Advanced_ResetDone, backup) });
            }
            catch (Exception ex) // also a credential-store failure; this runs fire-and-forget
            {
                _log.Error("settings reset failed", ex);
                PageHost.Children.Insert(0, new InfoBar { IsOpen = true, Severity = InfoBarSeverity.Error, Message = ex.Message });
            }
        }
        PageHost.Children.Add(Card(Strings.Advanced_Reset, L.F(Strings.Advanced_Reset_Desc, folder, Hotline.Core.Settings.SettingsReset.DefaultsPath(folder)),
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { Link(Strings.Advanced_ResetSettings, () => _ = ResetAsync(false)), Link(Strings.Advanced_ResetAll, () => _ = ResetAsync(true)) },
            }));
    }

    // ---- prompts page ----------------------------------------------------------------------

    private void BuildPromptsPage()
    {
        PageHost.Children.Add(new TextBlock
        {
            Text = Strings.Prompts_Intro,
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8),
        });
        var chat = _settings.Current.Chat;
        foreach (var name in _prompts.List())
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var edit = new Button { Content = Strings.Common_Edit };
            edit.Click += (_, _) => CliRunner.OpenInEditor(_prompts.PathFor(name));
            buttons.Children.Add(edit);
            if (name != chat.DefaultPrompt)
            {
                var makeDefault = new Button { Content = Strings.Prompts_MakeDefault };
                makeDefault.Click += (_, _) => { _settings.Update(s => s.Chat.DefaultPrompt = name); ShowPage("Prompts"); };
                buttons.Children.Add(makeDefault);
            }
            if (name != PromptLibrary.DefaultName)
            {
                var delete = new Button { Content = Strings.Common_Delete };
                delete.Click += (_, _) =>
                {
                    _prompts.Delete(name);
                    if (chat.DefaultPrompt == name) _settings.Update(s => s.Chat.DefaultPrompt = PromptLibrary.DefaultName);
                    ShowPage("Prompts");
                };
                buttons.Children.Add(delete);
            }
            PageHost.Children.Add(Card(name, name == chat.DefaultPrompt ? Strings.Prompts_Default : null, buttons));
        }
        var create = new Button { Content = Strings.Prompts_New, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        create.Click += (_, _) => { CliRunner.OpenInEditor(_prompts.PathFor(_prompts.Create())); ShowPage("Prompts"); };
        var open = new Button { Content = Strings.Prompts_OpenFolder };
        open.Click += (_, _) => CliRunner.OpenFolder(_prompts.Directory);
        PageHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { create, open } });
    }
}
