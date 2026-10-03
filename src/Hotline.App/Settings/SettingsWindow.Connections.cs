using Hotline.App.Chat;
using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Hotline.App.Settings;

public sealed partial class SettingsWindow
{
    private string? _selectedConnection;
    private bool _dialogOpen;

    /// <summary>Only one ContentDialog may be open; a second request is ignored (treated as Cancel).</summary>
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (_dialogOpen) return ContentDialogResult.None;
        _dialogOpen = true;
        try { return await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
    }

    private void BuildConnectionsPage()
    {
        var chat = _settings.Current.Chat;
        _selectedConnection ??= chat.DefaultBackend;
        if (chat.Backends.All(b => b.Id != _selectedConnection)) _selectedConnection = chat.DefaultBackend;

        var add = new DropDownButton { Content = "Add connection" };
        var addMenu = new MenuFlyout();
        foreach (var type in ConnectionTypes.All)
        {
            var item = new MenuFlyoutItem { Text = type.DisplayName + (BackendFactory.IsAvailable(type.Type) ? "" : "  (chat coming soon)") };
            item.Click += (_, _) => { _settings.Update(s => _selectedConnection = ConnectionEditor.Add(s.Chat, type.Type).Id); ShowPage("Connections"); };
            addMenu.Items.Add(item);
        }
        add.Flyout = addMenu;
        var duplicate = new Button { Content = "Duplicate" };
        duplicate.Click += (_, _) =>
        {
            var source = _selectedConnection!;
            string? copyId = null;
            _settings.Update(s => copyId = ConnectionEditor.Duplicate(s.Chat, source).Id);
            if (_secrets.Get(SecretKeys.ApiKey(source)) is { } key) _secrets.Set(SecretKeys.ApiKey(copyId!), key);
            _selectedConnection = copyId;
            ShowPage("Connections");
        };
        var remove = new Button { Content = "Remove", IsEnabled = chat.Backends.Count > 1 };
        remove.Click += async (_, _) =>
        {
            var id = _selectedConnection!;
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = "Remove this connection?", Content = chat.Backends.First(b => b.Id == id).Name,
                PrimaryButtonText = "Remove", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
            };
            if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
            _settings.Update(s => ConnectionEditor.Remove(s.Chat, id));
            _secrets.Set(SecretKeys.ApiKey(id), null);
            await _invalidate(id);
            _selectedConnection = null;
            ShowPage("Connections");
        };
        var makeDefault = new Button { Content = "Use by default", IsEnabled = _selectedConnection != chat.DefaultBackend };
        makeDefault.Click += (_, _) => { _settings.Update(s => ConnectionEditor.SetDefault(s.Chat, _selectedConnection!)); ShowPage("Connections"); };
        PageHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, duplicate, remove, makeDefault } });

        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, Margin = new Thickness(0, 8, 0, 8) };
        foreach (var p in chat.Backends)
        {
            var info = ConnectionTypes.Of(p.Type);
            var subtitle = info.DisplayName + (p.Id == chat.DefaultBackend ? " · default" : "") + (BackendFactory.IsAvailable(p.Type) ? "" : " · chat coming soon");
            list.Items.Add(new ListViewItem
            {
                Tag = p.Id,
                Content = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = p.Name }, new TextBlock { Text = subtitle, FontSize = 12, Opacity = 0.7 } } },
            });
        }
        list.SelectedItem = list.Items.OfType<ListViewItem>().FirstOrDefault(i => (string)i.Tag == _selectedConnection);
        var editorHost = new StackPanel { Spacing = 8 };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not ListViewItem { Tag: string id }) return;
            _selectedConnection = id;
            BuildEditor(editorHost, chat.Backends.First(b => b.Id == id));
        };
        PageHost.Children.Add(list);
        PageHost.Children.Add(editorHost);
        if (chat.Backends.FirstOrDefault(b => b.Id == _selectedConnection) is { } selected) BuildEditor(editorHost, selected);
    }

    /// <summary>
    /// agy runs in the background, where any tool call that would ask for permission is denied. Its allow/deny rules
    /// (in agy's own settings.json) decide what works; "approve everything" skips all checks and is dangerous.
    /// </summary>
    private void AddAgyPermissions(StackPanel host, BackendProfile p, Func<Action<BackendProfile>, Task> save)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var agySettings = Hotline.Core.Backends.Agy.AgyPermissions.SettingsPath(home);
        string Summary()
        {
            string? json;
            try { json = File.Exists(agySettings) ? File.ReadAllText(agySettings) : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Couldn't read agy's settings right now."; }
            return $"agy rules: {Hotline.Core.Backends.Agy.AgyPermissions.Rules(json, "allow").Count} allowed, " +
                   $"{Hotline.Core.Backends.Agy.AgyPermissions.Rules(json, "deny").Count} denied. Reading files in the working folder is allowed by default; " +
                   "shell commands need an allow rule.";
        }
        var summary = new TextBlock { Text = Summary(), FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        var addReadOnly = new Button { Content = "Allow read-only commands" };
        ToolTipService.SetToolTip(addReadOnly, "Adds rules like command(Get-ChildItem), command(git status) to agy's allow list, and denies Remove-Item, rm, git push...");
        addReadOnly.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = "Allow read-only shell commands for agy?",
                Content = new ScrollViewer
                {
                    MaxHeight = 320,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = "This edits agy's own settings (" + agySettings + "), so it also applies when you use agy yourself.\n\nAllow:\n" +
                               string.Join("\n", Hotline.Core.Backends.Agy.AgyPermissions.ReadOnlyCommandRules) +
                               "\n\nDeny:\n" + string.Join("\n", Hotline.Core.Backends.Agy.AgyPermissions.DenyRules),
                    },
                },
                PrimaryButtonText = "Add rules", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
            };
            if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(agySettings)!);
                var json = File.Exists(agySettings) ? File.ReadAllText(agySettings) : null;
                var updated = Hotline.Core.Backends.Agy.AgyPermissions.AddRules(json,
                    Hotline.Core.Backends.Agy.AgyPermissions.ReadOnlyCommandRules, Hotline.Core.Backends.Agy.AgyPermissions.DenyRules);
                // Keep the user's original file once (never overwritten by later presses), then replace atomically.
                if (json is not null && !File.Exists(agySettings + ".hotline-original.bak")) File.Copy(agySettings, agySettings + ".hotline-original.bak");
                File.WriteAllText(agySettings + ".tmp", updated);
                File.Move(agySettings + ".tmp", agySettings, overwrite: true);
                summary.Text = Summary();
                await _invalidate(p.Id);
            }
            catch (Exception ex) { _log.Error("editing agy permissions failed", ex); summary.Text = "Couldn't edit agy's settings: " + ex.Message; }
        };
        var open = new Button { Content = "Edit agy rules" };
        open.Click += (_, _) => { if (File.Exists(agySettings)) CliRunner.OpenInEditor(agySettings); else CliRunner.OpenFolder(Path.GetDirectoryName(agySettings)!); };
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(summary);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { addReadOnly, open } });
        host.Children.Add(Card("agy permissions", null, panel));

        var approveAll = new ToggleSwitch { IsOn = p.ApproveAllTools, OnContent = "", OffContent = "", MinWidth = 0, IsEnabled = p.Tools == ToolMode.Inherit };
        var reverting = false;
        approveAll.Toggled += async (_, _) =>
        {
            if (reverting) { reverting = false; return; }
            if (approveAll.IsOn)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = Root.XamlRoot, Title = "Approve everything? (dangerous)",
                    Content = "agy will run ANY shell command and edit or delete ANY file it decides to, without asking. " +
                              "A wrong answer or a malicious web page or file can make it do real damage. Only use this with a working folder you can afford to lose.",
                    PrimaryButtonText = "I understand, turn on", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
                };
                if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) { reverting = true; approveAll.IsOn = false; return; }
            }
            await save(x => x.ApproveAllTools = approveAll.IsOn);
        };
        host.Children.Add(Card("⚠ Approve everything (dangerous)",
            "Skips all of agy's permission checks (--dangerously-skip-permissions). Only in “use the program's own tools” mode.", approveAll));
    }

    private void BuildEditor(StackPanel host, BackendProfile p)
    {
        host.Children.Clear();
        var info = ConnectionTypes.Of(p.Type);
        host.Children.Add(new TextBlock { Text = info.Description, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });

        async Task Save(Action<BackendProfile> change)
        {
            _settings.Update(_ => change(p));
            await _invalidate(p.Id);
        }

        TextBox Text(string? value, string placeholder, Action<string?> set)
        {
            var box = new TextBox { Text = value ?? "", PlaceholderText = placeholder, Width = 360 };
            box.LostFocus += async (_, _) =>
            {
                var v = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
                if (v != (string.IsNullOrWhiteSpace(value) ? null : value)) { value = v; await Save(_ => set(v)); }
            };
            return box;
        }

        host.Children.Add(Card("Name", null, Text(p.Name, info.DefaultName, v => p.Name = v ?? info.DefaultName)));
        if (info.Has(ConnectionField.Endpoint))
            host.Children.Add(Card("Endpoint", "Base URL of the API.", Text(p.Endpoint, info.DefaultEndpoint ?? "", v => p.Endpoint = v)));
        if (info.Has(ConnectionField.ApiKey))
        {
            var saved = _secrets.Get(SecretKeys.ApiKey(p.Id)) is not null;
            var keyBox = new PasswordBox
            {
                Width = 360,
                PlaceholderText = saved ? "Saved; type to replace" : info.ApiKeyOptional ? "Optional" : "Required",
            };
            keyBox.LostFocus += async (_, _) =>
            {
                if (keyBox.Password.Length == 0) return;
                try { _secrets.Set(SecretKeys.ApiKey(p.Id), keyBox.Password.Trim()); }
                catch (Exception) { keyBox.Header = "Couldn't save the key"; return; }
                keyBox.Password = "";
                keyBox.PlaceholderText = "Saved; type to replace";
                await _invalidate(p.Id);
            };
            host.Children.Add(Card("API key", "Stored in Windows Credential Locker, not in settings.json.", keyBox));
        }
        if (info.Has(ConnectionField.CliPath))
            host.Children.Add(Card("Program path", "Leave empty to find it automatically.", Text(p.CliPath, "Auto-detect", v => p.CliPath = v)));
        if (info.Has(ConnectionField.Agent))
            host.Children.Add(Card("agy agent", "Used in Chat-only mode.", Text(p.Agent, "hotline", v => p.Agent = v)));
        if (info.Has(ConnectionField.ExtraArgs))
            host.Children.Add(Card("Extra arguments", "Passed to the program as-is (space separated).", Text(p.ExtraArgs, "", v => p.ExtraArgs = v)));

        if (info.Has(ConnectionField.Tools) && !info.Has(ConnectionField.CliPath))
        {
            // API connections: Hotline's own MCP tools (Settings › Tools), approved in the panel.
            var apiMode = new ComboBox { MinWidth = 280 };
            apiMode.Items.Add(new ComboBoxItem { Content = "Chat only", Tag = ToolMode.ChatOnly });
            apiMode.Items.Add(new ComboBoxItem { Content = "Use Hotline's tools (MCP)", Tag = ToolMode.Inherit });
            apiMode.SelectedIndex = p.Tools == ToolMode.Inherit ? 1 : 0;
            apiMode.SelectionChanged += async (_, _) =>
            {
                if (apiMode.SelectedItem is ComboBoxItem { Tag: ToolMode m }) await Save(x => x.Tools = m);
            };
            host.Children.Add(Card("Tool use", "Lets this AI use the tools from your MCP servers (Settings › Tools). Read-only tools run; anything else asks you in the panel first.", apiMode));
        }
        else if (info.Has(ConnectionField.Tools))
        {
            var mode = new ComboBox { MinWidth = 280 };
            mode.Items.Add(new ComboBoxItem { Content = "Chat only (can read your attachments)", Tag = ToolMode.ChatOnly });
            mode.Items.Add(new ComboBoxItem { Content = "Use the program's own tools and permissions", Tag = ToolMode.Inherit });
            mode.SelectedIndex = p.Tools == ToolMode.Inherit ? 1 : 0;
            var folder = Text(p.WorkingDirectory, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), v => p.WorkingDirectory = v);
            folder.IsEnabled = p.Tools == ToolMode.Inherit;
            var browse = new Button { Content = "Browse…", IsEnabled = folder.IsEnabled };
            browse.Click += async (_, _) =>
            {
                var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                if (await picker.PickSingleFolderAsync() is { } chosen) { folder.Text = chosen.Path; await Save(x => x.WorkingDirectory = chosen.Path); }
            };
            mode.SelectionChanged += async (_, _) =>
            {
                if (mode.SelectedItem is not ComboBoxItem { Tag: ToolMode m }) return;
                folder.IsEnabled = browse.IsEnabled = m == ToolMode.Inherit;
                await Save(x => x.Tools = m);
                BuildEditor(host, p);
            };
            host.Children.Add(Card("Tool use", "Inherit lets the program run its tools (files, commands, web) under its own permission rules.", mode));
            host.Children.Add(Card("Working folder", "Where the program's tools act in Inherit mode.",
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { folder, browse } }));
            if (p.Type == BackendType.Antigravity) AddAgyPermissions(host, p, Save);
        }

        if (p.Type == BackendType.Anthropic)
        {
            var fallback = new ToggleSwitch { IsOn = p.RefusalFallback, OnContent = "", OffContent = "", MinWidth = 0 };
            fallback.Toggled += async (_, _) => await Save(x => x.RefusalFallback = fallback.IsOn);
            host.Children.Add(Card("Refusal fallback", "If a safety check declines a request, the API re-serves it with a suitable model instead of stopping (Fable 5.1, Opus 5.5, Opus 5, Sonnet 5.5).", fallback));
        }

        var prompt = new ComboBox { MinWidth = 220 };
        prompt.Items.Add(new ComboBoxItem { Content = $"Default ({_settings.Current.Chat.DefaultPrompt})", Tag = "" });
        foreach (var name in _prompts.List()) prompt.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        prompt.SelectedItem = prompt.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (p.Prompt ?? "")) ?? prompt.Items[0];
        prompt.SelectionChanged += async (_, _) =>
        {
            if (prompt.SelectedItem is ComboBoxItem { Tag: string name }) await Save(x => x.Prompt = name.Length == 0 ? null : name);
        };
        host.Children.Add(Card("System prompt", null, prompt));

        var model = new ComboBox { IsEditable = true, MinWidth = 260 };
        model.Items.Add("Default");
        if (!string.IsNullOrWhiteSpace(p.Model)) model.Items.Add(p.Model);
        model.SelectedItem = string.IsNullOrWhiteSpace(p.Model) ? "Default" : p.Model;
        var status = new InfoBar { IsClosable = true };
        var filling = false;
        async Task LoadModels(bool refresh)
        {
            try
            {
                filling = true;
                var list = await _models.GetAsync(p, refresh, CancellationToken.None);
                var current = model.SelectedItem as string ?? "Default";
                model.Items.Clear();
                model.Items.Add("Default");
                foreach (var m in list) model.Items.Add(m.Id);
                if (current != "Default" && !list.Any(m => m.Id == current)) model.Items.Add(current);
                model.SelectedItem = current;
                filling = false;
                status.Severity = InfoBarSeverity.Success;
                status.Message = $"Connected: {list.Count} model(s) available.";
            }
            catch (ModelListException ex) { status.Severity = InfoBarSeverity.Error; status.Message = ex.Message; }
            catch (Exception ex) { _log.Error("model list failed", ex); status.Severity = InfoBarSeverity.Error; status.Message = ex.Message; }
            finally { filling = false; }
            status.IsOpen = true;
        }
        model.DropDownOpened += async (_, _) => await LoadModels(false);
        model.SelectionChanged += async (_, _) =>
        {
            if (filling || model.SelectedItem is not string m) return;
            var value = m == "Default" ? null : m;
            if (value != p.Model) await Save(x => x.Model = value);
        };
        model.TextSubmitted += async (_, e) => await Save(x => x.Model = string.IsNullOrWhiteSpace(e.Text) || e.Text == "Default" ? null : e.Text.Trim());
        host.Children.Add(Card("Default model", null, model));

        if (info.EffortLevels.Count > 0)
        {
            var effort = new ComboBox { MinWidth = 160 };
            effort.Items.Add("Default");
            foreach (var l in info.EffortLevels) effort.Items.Add(l);
            effort.SelectedItem = p.Effort is { } e && info.EffortLevels.Contains(e) ? e : "Default";
            effort.SelectionChanged += async (_, _) =>
            {
                if (effort.SelectedItem is string v) await Save(x => x.Effort = v == "Default" ? null : v);
            };
            host.Children.Add(Card("Default effort", null, effort));
        }

        var test = new Button { Content = "Test connection" };
        test.Click += async (_, _) => await LoadModels(true);
        host.Children.Add(test);
        host.Children.Add(status);
    }
}
