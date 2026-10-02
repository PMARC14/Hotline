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

    internal SettingsWindow(SettingsService settings, ISecretStore secrets, ModelCatalog models, Func<string, ValueTask> invalidate,
        PromptLibrary prompts, string settingsFile, string logsDir, FileLog log)
    {
        (_settings, _secrets, _models, _invalidate, _prompts, _settingsFile, _logsDir, _log) =
            (settings, secrets, models, invalidate, prompts, settingsFile, logsDir, log);
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon("Assets\\Hotline.ico");
        var dpi = Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1000 * scale), (int)(740 * scale)));
        ApplyTheme();
        _settings.Changed += OnSettingsChanged;
        Closed += (_, _) => _settings.Changed -= OnSettingsChanged;
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void OnSettingsChanged() => DispatcherQueue.TryEnqueue(ApplyTheme);

    private void ApplyTheme() => Root.RequestedTheme = _settings.Current.Window.Theme switch
    {
        ThemeChoice.Light => ElementTheme.Light,
        ThemeChoice.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        => ShowPage(args.SelectedItemContainer?.Tag as string ?? "General");

    private void ShowPage(string tag)
    {
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
            default:
                var page = Enum.Parse<SettingsPage>(tag);
                foreach (var item in SettingsSchema.Items.Where(i => i.Page == page)) PageHost.Children.Add(BuildItem(item));
                if (page == SettingsPage.Advanced) BuildAdvancedExtras();
                if (page == SettingsPage.General) BuildStartupCard();
                break;
        }
    }

    // ---- generic setting cards -------------------------------------------------------------

    private Border Card(string header, string? description, UIElement? control)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = header, TextWrapping = TextWrapping.Wrap });
        if (description is not null) text.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
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
                toggle.Toggled += (_, _) => _settings.Update(x => t.Set(x, toggle.IsOn));
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
        var status = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
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
        var panel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(toggle);
        PageHost.Children.Add(Card("Start with Windows", null, new StackPanel { Spacing = 4, Children = { toggle, status } }));
        Refresh();
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
