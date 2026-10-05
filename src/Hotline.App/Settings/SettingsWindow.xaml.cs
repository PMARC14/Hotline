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

    internal SettingsWindow(SettingsService settings, ISecretStore secrets, ModelCatalog models, Func<string, ValueTask> invalidate,
        PromptLibrary prompts, string settingsFile, string logsDir, FileLog log, Hotline.Core.Tools.McpToolHost? toolHost = null,
        string? mcpPath = null, string? odrPath = null)
    {
        (_toolHost, _mcpPath, _odrPath) = (toolHost, mcpPath ?? "", odrPath);
        (_settings, _secrets, _models, _invalidate, _prompts, _settingsFile, _logsDir, _log) =
            (settings, secrets, models, invalidate, prompts, settingsFile, logsDir, log);
        InitializeComponent();
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
            default:
                var page = Enum.Parse<SettingsPage>(tag);
                foreach (var item in SettingsSchema.Items.Where(i => i.Page == page)) PageHost.Children.Add(BuildItem(item));
                if (page == SettingsPage.Advanced) BuildAdvancedExtras();
                if (page == SettingsPage.General) BuildStartupCard();
                if (page == SettingsPage.Appearance) BuildToolbarSection();
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
        var description = item.RequiresRestart ? $"{item.Description} (Applies after restarting Hotline.)".TrimStart() : item.Description;
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
                return Card(t.Header, description, toggle);
            }
            case NumberItem n:
            {
                var box = new NumberBox
                {
                    Minimum = n.Min, Maximum = n.Max, SmallChange = n.Step, LargeChange = n.Step * 10, Value = n.Get(s), Width = 150,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
                };
                box.ValueChanged += (_, e) => { if (!double.IsNaN(e.NewValue)) _settings.Update(x => n.Set(x, e.NewValue)); };
                var label = n.Unit is null ? (UIElement)box : new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { box, new TextBlock { Text = n.Unit, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 } },
                };
                return Card(n.Header, description, label);
            }
            case ChoiceItem c:
            {
                var combo = new ComboBox { MinWidth = 200 };
                foreach (var o in c.Options) combo.Items.Add(new ComboBoxItem { Content = o.Label, Tag = o.Value });
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == c.Get(s));
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string v }) _settings.Update(x => c.Set(x, v)); };
                return Card(c.Header, description, combo);
            }
            case TextItem tx:
            {
                var box = new TextBox { Text = tx.Get(s) ?? "", PlaceholderText = tx.Placeholder ?? "", Width = 240 };
                box.LostFocus += (_, _) =>
                {
                    if (tx.Header == "Extra hotkey" && !string.IsNullOrWhiteSpace(box.Text) && !Hotkey.TryParse(box.Text, out _))
                    {
                        box.Header = "Not a valid hotkey (example: Ctrl+Alt+H)";
                        return;
                    }
                    box.Header = null;
                    if ((tx.Get(_settings.Current) ?? "") != box.Text.Trim()) _settings.Update(x => tx.Set(x, box.Text));
                };
                return Card(tx.Header, description, box);
            }
            default:
                return Card(item.Header, description, null);
        }
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
            else StartupRegistration.Disable();
            Refresh();
        };
        PageHost.Children.Add(CardWithStatus("Start with Windows", status, toggle));
        Refresh();
    }

    /// <summary>MCP servers (mcp.json + Windows agent registry), their status and the approval rules.</summary>
    private void BuildToolsPage()
    {
        Button Link(string text, Action action) { var b = new Button { Content = text }; b.Click += (_, _) => action(); return b; }
        PageHost.Children.Add(new TextBlock
        {
            Text = "API connections with Tool use set to \"Use Hotline's tools\" can call the tools of these MCP servers. Read-only tools run; " +
                   "anything else asks you in the panel (Allow once / Always / Deny).",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8),
        });
        PageHost.Children.Add(Card("MCP servers", _mcpPath + " — same \"mcpServers\" format as other MCP apps; approvals live in the same file",
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { Link("Edit mcp.json", () => CliRunner.OpenInEditor(_mcpPath)), Link("Open folder", () => CliRunner.OpenFolder(Path.GetDirectoryName(_mcpPath)!)) },
            }));
        PageHost.Children.Add(Card("Windows agent registry",
            _odrPath is null ? "Not available on this Windows (needs build 26220.7262 or later). Its built-in connectors will appear here automatically."
                             : $"Available ({_odrPath}). Its connectors are listed below as windows-…",
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
                list.Children.Add(new TextBlock { Text = "No servers checked yet. Add servers to mcp.json, then press Check servers.", Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            foreach (var s in status.OrderBy(s => s.Name))
                list.Children.Add(Card(s.DisplayName, s.State switch
                {
                    Hotline.Core.Tools.McpServerState.Ready => $"Ready — {s.ToolCount} tool(s)",
                    Hotline.Core.Tools.McpServerState.Failed => $"Failed: {s.Error}",
                    Hotline.Core.Tools.McpServerState.Disabled => "Disabled in mcp.json",
                    _ => s.State.ToString(),
                }, null));
        }
        var check = new Button { Content = "Check servers", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
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
        PageHost.Children.Add(Card("Settings file", _settingsFile, new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { Link("Open folder", () => CliRunner.OpenFolder(folder)), Link("Edit settings.json", () => CliRunner.OpenInEditor(_settingsFile)) },
        }));
        var connections = Path.Combine(folder, Hotline.Core.Settings.SettingsStore.ConnectionsFolder);
        PageHost.Children.Add(Card("AI connections", connections + " — one file per connection; settings.json \"chat.order\" sets the dropdown order",
            Link("Open connections folder", () => CliRunner.OpenFolder(connections))));
        PageHost.Children.Add(Card("Logs", _logsDir, Link("Open logs folder", () => CliRunner.OpenFolder(_logsDir))));
    }

    // ---- prompts page ----------------------------------------------------------------------

    private void BuildPromptsPage()
    {
        PageHost.Children.Add(new TextBlock
        {
            Text = "System prompts are Markdown files. The default prompt is used by every connection that doesn't pick its own.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8),
        });
        var chat = _settings.Current.Chat;
        foreach (var name in _prompts.List())
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var edit = new Button { Content = "Edit" };
            edit.Click += (_, _) => CliRunner.OpenInEditor(_prompts.PathFor(name));
            buttons.Children.Add(edit);
            if (name != chat.DefaultPrompt)
            {
                var makeDefault = new Button { Content = "Make default" };
                makeDefault.Click += (_, _) => { _settings.Update(s => s.Chat.DefaultPrompt = name); ShowPage("Prompts"); };
                buttons.Children.Add(makeDefault);
            }
            if (name != PromptLibrary.DefaultName)
            {
                var delete = new Button { Content = "Delete" };
                delete.Click += (_, _) =>
                {
                    _prompts.Delete(name);
                    if (chat.DefaultPrompt == name) _settings.Update(s => s.Chat.DefaultPrompt = PromptLibrary.DefaultName);
                    ShowPage("Prompts");
                };
                buttons.Children.Add(delete);
            }
            PageHost.Children.Add(Card(name, name == chat.DefaultPrompt ? "Default prompt" : null, buttons));
        }
        var create = new Button { Content = "New prompt", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        create.Click += (_, _) => { CliRunner.OpenInEditor(_prompts.PathFor(_prompts.Create())); ShowPage("Prompts"); };
        var open = new Button { Content = "Open prompts folder" };
        open.Click += (_, _) => CliRunner.OpenFolder(_prompts.Directory);
        PageHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { create, open } });
    }
}
