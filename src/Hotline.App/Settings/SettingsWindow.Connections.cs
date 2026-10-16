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
        // The default, or the first connection when none is chosen yet (first run): Duplicate/Remove need a real one.
        if (chat.Backends.All(b => b.Id != _selectedConnection))
            _selectedConnection = chat.Backends.Any(b => b.Id == chat.DefaultBackend) ? chat.DefaultBackend : chat.Backends[0].Id;

        var add = new DropDownButton { Content = Strings.Conn_Add };
        var addMenu = new MenuFlyout();
        foreach (var type in ConnectionTypes.All)
        {
            var item = new MenuFlyoutItem { Text = type.DisplayName };
            item.Click += (_, _) => { _settings.Update(s => _selectedConnection = ConnectionEditor.Add(s.Chat, type.Type).Id); ShowPage("Connections"); };
            addMenu.Items.Add(item);
        }
        add.Flyout = addMenu;
        var duplicate = new Button { Content = Strings.Conn_Duplicate };
        duplicate.Click += (_, _) =>
        {
            var source = _selectedConnection!;
            string? copyId = null;
            _settings.Update(s => copyId = ConnectionEditor.Duplicate(s.Chat, source).Id);
            if (_secrets.Get(SecretKeys.ApiKey(source)) is { } key) _secrets.Set(SecretKeys.ApiKey(copyId!), key);
            _selectedConnection = copyId;
            ShowPage("Connections");
        };
        var remove = new Button { Content = Strings.Common_Remove, IsEnabled = chat.Backends.Count > 1 };
        remove.Click += async (_, _) =>
        {
            var id = _selectedConnection!;
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = Strings.Conn_RemoveTitle, Content = chat.Backends.First(b => b.Id == id).Name,
                PrimaryButtonText = Strings.Common_Remove, CloseButtonText = Strings.Common_Cancel, DefaultButton = ContentDialogButton.Close,
            };
            if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
            _settings.Update(s => ConnectionEditor.Remove(s.Chat, id));
            _secrets.Set(SecretKeys.ApiKey(id), null);
            await _invalidate(id);
            _selectedConnection = null;
            ShowPage("Connections");
        };
        var makeDefault = new Button { Content = Strings.Conn_UseByDefault, IsEnabled = _selectedConnection != chat.DefaultBackend };
        makeDefault.Click += (_, _) => { _settings.Update(s => ConnectionEditor.SetDefault(s.Chat, _selectedConnection!)); ShowPage("Connections"); };
        Button MoveButton(string glyph, string tip, int delta)
        {
            var b = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 } };
            ToolTipService.SetToolTip(b, tip);
            b.Click += (_, _) => { _settings.Update(s => ConnectionEditor.Move(s.Chat, _selectedConnection!, delta)); ShowPage("Connections"); };
            return b;
        }
        PageHost.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { add, duplicate, remove, makeDefault, MoveButton("\uE74A", Strings.Conn_MoveUp, -1), MoveButton("\uE74B", Strings.Conn_MoveDown, +1) },
        });

        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, Margin = new Thickness(0, 8, 0, 8) };
        foreach (var p in chat.Backends)
        {
            var info = ConnectionTypes.Of(p.Type);
            var subtitle = p.Id == chat.DefaultBackend ? L.F(Strings.Conn_DefaultSuffix, info.DisplayName) : info.DisplayName;
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Strings.Agy_ReadFailed; }
            return L.F(Strings.Agy_Summary, Hotline.Core.Backends.Agy.AgyPermissions.Rules(json, "allow").Count,
                Hotline.Core.Backends.Agy.AgyPermissions.Rules(json, "deny").Count);
        }
        var summary = new TextBlock { Text = Summary() }; // the card's description (styled by CardWithStatus)
        var addReadOnly = new Button { Content = Strings.Agy_AllowReadOnly };
        ToolTipService.SetToolTip(addReadOnly, Strings.Agy_AllowReadOnly_Tip);
        addReadOnly.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = Strings.Agy_AllowTitle,
                Content = new ScrollViewer
                {
                    MaxHeight = 320,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = L.F(Strings.Agy_AllowBody, agySettings,
                            string.Join("\n", Hotline.Core.Backends.Agy.AgyPermissions.ReadOnlyCommandRules),
                            string.Join("\n", Hotline.Core.Backends.Agy.AgyPermissions.DenyRules)),
                    },
                },
                PrimaryButtonText = Strings.Agy_AddRules, CloseButtonText = Strings.Common_Cancel, DefaultButton = ContentDialogButton.Close,
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
            catch (Exception ex) { _log.Error("editing agy permissions failed", ex); summary.Text = L.F(Strings.Agy_EditFailed, ex.Message); }
        };
        var open = new Button { Content = Strings.Agy_EditRules };
        open.Click += (_, _) => { if (File.Exists(agySettings)) CliRunner.OpenInEditor(agySettings); else CliRunner.OpenFolder(Path.GetDirectoryName(agySettings)!); };
        // Like every other card: title and the rules summary on the left, the actions on the right.
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { addReadOnly, open } };
        host.Children.Add(CardWithStatus(Strings.Agy_Permissions, summary, buttons));
    }

    /// <summary>
    /// ⚠ Approve everything (every CLI, inherit mode only): skips the CLI's permission checks (agy/Claude Code
    /// --dangerously-skip-permissions, Codex --dangerously-bypass-approvals-and-sandbox). Turning it on asks first.
    /// </summary>
    private void AddApproveAll(StackPanel host, BackendProfile p, Func<Action<BackendProfile>, Task> save)
    {
        var cliName = ConnectionTypes.Of(p.Type).DisplayName;
        var approveAll = new ToggleSwitch { IsOn = p.ApproveAllTools, OnContent = "", OffContent = "", MinWidth = 0, IsEnabled = p.Tools == ToolMode.Inherit };
        var reverting = false;
        approveAll.Toggled += async (_, _) =>
        {
            if (reverting) { reverting = false; return; }
            if (approveAll.IsOn)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = Root.XamlRoot, Title = Strings.Agy_ApproveAllTitle,
                    Content = new TextBlock { Text = L.F(Strings.Conn_ApproveAllBody, cliName), TextWrapping = TextWrapping.Wrap },
                    PrimaryButtonText = Strings.Agy_ApproveAllConfirm, CloseButtonText = Strings.Common_Cancel, DefaultButton = ContentDialogButton.Close,
                };
                if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) { reverting = true; approveAll.IsOn = false; return; }
            }
            await save(x => x.ApproveAllTools = approveAll.IsOn);
        };
        host.Children.Add(Card(Strings.Agy_ApproveAll, L.F(Strings.Conn_ApproveAll_Desc, cliName), approveAll));
    }

    /// <summary>A number setting that can be left empty (= the model's default).</summary>
    private NumberBox OptionalNumber(double? value, double min, double max, double step, string placeholder, Func<double?, Task> set)
    {
        var box = new NumberBox
        {
            Value = value ?? double.NaN, Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, PlaceholderText = placeholder, MinWidth = 160,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
        };
        box.ValueChanged += async (_, e) => await set(double.IsNaN(e.NewValue) ? null : e.NewValue);
        return box;
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

        host.Children.Add(Card(Strings.Conn_Name, null, Text(p.Name, info.DefaultName, v => p.Name = v ?? info.DefaultName)));
        if (info.Has(ConnectionField.Endpoint))
            host.Children.Add(Card(Strings.Conn_Endpoint, Strings.Conn_Endpoint_Desc, Text(p.Endpoint, info.DefaultEndpoint ?? "", v => p.Endpoint = v)));
        if (info.Has(ConnectionField.ApiKey))
        {
            var saved = _secrets.Get(SecretKeys.ApiKey(p.Id)) is not null;
            var keyBox = new PasswordBox
            {
                Width = 360,
                PlaceholderText = saved ? Strings.Conn_KeySaved : info.ApiKeyOptional ? Strings.Conn_Optional : Strings.Conn_Required,
            };
            keyBox.LostFocus += async (_, _) =>
            {
                if (keyBox.Password.Length == 0) return;
                try { _secrets.Set(SecretKeys.ApiKey(p.Id), keyBox.Password.Trim()); }
                catch (Exception) { keyBox.Header = Strings.Conn_KeySaveFailed; return; }
                keyBox.Password = "";
                keyBox.PlaceholderText = Strings.Conn_KeySaved;
                await _invalidate(p.Id);
            };
            host.Children.Add(Card(Strings.Conn_ApiKey, Strings.Conn_ApiKey_Desc, keyBox));
        }
        if (info.Has(ConnectionField.CliPath))
            host.Children.Add(Card(Strings.Conn_ProgramPath, Strings.Conn_ProgramPath_Desc, Text(p.CliPath, Strings.Conn_AutoDetect, v => p.CliPath = v)));
        if (info.Has(ConnectionField.Agent))
            host.Children.Add(Card(Strings.Conn_AgyAgent, Strings.Conn_AgyAgent_Desc, Text(p.Agent, "hotline", v => p.Agent = v)));
        if (ConnectionTypes.IsCli(p.Type))
        {
            // Chats live in the CLI's own history (resumable there and from Recent chats); Hotline can keep them too.
            var cliName = ConnectionTypes.Of(p.Type).DisplayName;
            var keep = new ToggleSwitch { IsOn = p.KeepCliSessions, OnContent = "", OffContent = "", MinWidth = 0 };
            var alsoHotline = new ToggleSwitch { IsOn = p.KeepInHotline || !p.KeepCliSessions, OnContent = "", OffContent = "", MinWidth = 0, IsEnabled = p.KeepCliSessions };
            var updating = false;
            keep.Toggled += async (_, _) =>
            {
                updating = true;
                alsoHotline.IsEnabled = keep.IsOn;
                alsoHotline.IsOn = !keep.IsOn || p.KeepInHotline; // the CLI not keeping it means Hotline does
                updating = false;
                await Save(x => x.KeepCliSessions = keep.IsOn);
            };
            alsoHotline.Toggled += async (_, _) => { if (!updating && keep.IsOn) await Save(x => x.KeepInHotline = alsoHotline.IsOn); };
            host.Children.Add(Card(L.F(Strings.Conn_KeepCli, cliName), Strings.Conn_KeepCli_Desc, keep));
            host.Children.Add(Card(Strings.Conn_KeepInHotline, Strings.Conn_KeepInHotline_Desc, alsoHotline));
        }
        if (info.Has(ConnectionField.ExtraArgs))
        {
            host.Children.Add(Card(Strings.Conn_ExtraArgs, Strings.Conn_ExtraArgs_Desc, Text(p.ExtraArgs, "", v => p.ExtraArgs = v)));
            // Advanced: replaces the mode's flags (the protocol flags are always added; dangerous ones never come from here).
            host.Children.Add(Card(Strings.Conn_LaunchArgs, Strings.Conn_LaunchArgs_Desc, Text(p.Args is null ? null : string.Join(' ', p.Args), Strings.Conn_LaunchArgs_Default,
                v => p.Args = string.IsNullOrWhiteSpace(v) ? null : v.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList())));
        }

        if (info.Has(ConnectionField.Tools) && !info.Has(ConnectionField.CliPath))
        {
            // API connections: Hotline's own MCP tools (Settings › Tools), approved in the panel.
            var apiMode = new ComboBox { MinWidth = 280 };
            apiMode.Items.Add(new ComboBoxItem { Content = Strings.Conn_ChatOnly, Tag = ToolMode.ChatOnly });
            apiMode.Items.Add(new ComboBoxItem { Content = Strings.Conn_UseHotlineTools, Tag = ToolMode.Inherit });
            apiMode.SelectedIndex = p.Tools == ToolMode.Inherit ? 1 : 0;
            apiMode.SelectionChanged += async (_, _) =>
            {
                if (apiMode.SelectedItem is ComboBoxItem { Tag: ToolMode m }) await Save(x => x.Tools = m);
            };
            host.Children.Add(Card(Strings.Conn_ToolUse, Strings.Conn_ToolUse_ApiDesc, apiMode));
        }
        else if (info.Has(ConnectionField.Tools))
        {
            var mode = new ComboBox { MinWidth = 280 };
            mode.Items.Add(new ComboBoxItem { Content = Strings.Conn_ChatOnlyCli, Tag = ToolMode.ChatOnly });
            mode.Items.Add(new ComboBoxItem { Content = Strings.Conn_UseOwnTools, Tag = ToolMode.Inherit });
            mode.SelectedIndex = p.Tools == ToolMode.Inherit ? 1 : 0;
            var folder = Text(p.WorkingDirectory, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), v => p.WorkingDirectory = v);
            folder.IsEnabled = p.Tools == ToolMode.Inherit;
            var browse = new Button { Content = Strings.Common_Browse, IsEnabled = folder.IsEnabled };
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
            host.Children.Add(Card(Strings.Conn_ToolUse, Strings.Conn_ToolUse_CliDesc, mode));
            host.Children.Add(Card(Strings.Conn_WorkingFolder, Strings.Conn_WorkingFolder_Desc,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { folder, browse } }));
            if (p.Type == BackendType.Antigravity) AddAgyPermissions(host, p, Save);
            AddApproveAll(host, p, Save);
        }

        if (p.Type == BackendType.Anthropic)
        {
            var fallback = new ToggleSwitch { IsOn = p.RefusalFallback, OnContent = "", OffContent = "", MinWidth = 0 };
            fallback.Toggled += async (_, _) => await Save(x => x.RefusalFallback = fallback.IsOn);
            host.Children.Add(Card(Strings.Conn_RefusalFallback, Strings.Conn_RefusalFallback_Desc, fallback));
        }

        if (info.Has(ConnectionField.ApiKey))
        {
            var images = new ToggleSwitch { IsOn = ConnectionTypes.TakesImages(p), OnContent = "", OffContent = "", MinWidth = 0 };
            images.Toggled += async (_, _) =>
                await Save(x => x.Images = images.IsOn == info.ImagesByDefault ? null : images.IsOn); // default stays unset in the file
            host.Children.Add(Card(Strings.Conn_Images, Strings.Conn_Images_Desc, images));
        }

        var prompt = new ComboBox { MinWidth = 220 };
        prompt.Items.Add(new ComboBoxItem { Content = L.F(Strings.Conn_DefaultPrompt, _settings.Current.Chat.DefaultPrompt), Tag = "" });
        foreach (var name in _prompts.List()) prompt.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        prompt.SelectedItem = prompt.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (p.Prompt ?? "")) ?? prompt.Items[0];
        prompt.SelectionChanged += async (_, _) =>
        {
            if (prompt.SelectedItem is ComboBoxItem { Tag: string name }) await Save(x => x.Prompt = name.Length == 0 ? null : name);
        };
        host.Children.Add(Card(Strings.Conn_SystemPrompt, null, prompt));
        if (info.Has(ConnectionField.MaxTokens))
            host.Children.Add(Card(Strings.Conn_MaxTokens, Strings.Conn_MaxTokens_Desc,
                OptionalNumber(p.MaxTokens, 1, 1_000_000, 1024, Strings.Conn_ModelDefault, v => Save(x => x.MaxTokens = v is { } n ? (int)Math.Round(n) : null))));
        if (info.Has(ConnectionField.Temperature))
            host.Children.Add(Card(Strings.Conn_Temperature, Strings.Conn_Temperature_Desc,
                OptionalNumber(p.Temperature, 0, 2, 0.1, Strings.Conn_ModelDefault, v => Save(x => x.Temperature = v is { } t ? Math.Round(t, 2) : null))));

        var def = Strings.Common_Default; // the "no override" entry (a typed "Default" in English means the same)
        bool IsDefault(string? v) => string.IsNullOrWhiteSpace(v) || v == def || v == "Default";
        var model = new ComboBox { IsEditable = true, MinWidth = 260 };
        model.Items.Add(def);
        if (!string.IsNullOrWhiteSpace(p.Model)) model.Items.Add(p.Model);
        model.SelectedItem = string.IsNullOrWhiteSpace(p.Model) ? def : p.Model;
        var status = new InfoBar { IsClosable = true };
        var filling = false;
        async Task LoadModels(bool refresh)
        {
            try
            {
                filling = true;
                var list = await _models.GetAsync(p, refresh, CancellationToken.None);
                var current = model.SelectedItem as string ?? def;
                model.Items.Clear();
                model.Items.Add(def);
                foreach (var m in list) model.Items.Add(m.Id);
                if (current != def && !list.Any(m => m.Id == current)) model.Items.Add(current);
                model.SelectedItem = current;
                filling = false;
                status.Severity = InfoBarSeverity.Success;
                status.Message = ModelCatalog.Describe(list, p.Model, ConnectionTypes.Of(p.Type).ListsModels);
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
            var value = IsDefault(m) ? null : m;
            if (value != p.Model) await Save(x => x.Model = value);
        };
        model.TextSubmitted += async (_, e) => await Save(x => x.Model = IsDefault(e.Text) ? null : e.Text.Trim());
        host.Children.Add(Card(Strings.Conn_DefaultModel, null, model));

        if (info.EffortLevels.Count > 0)
        {
            var effort = new ComboBox { MinWidth = 160 };
            effort.Items.Add(new ComboBoxItem { Content = def, Tag = "" });
            foreach (var l in info.EffortLevels) effort.Items.Add(new ComboBoxItem { Content = L.Effort(l), Tag = l });
            var chosen = p.Effort is { } e && info.EffortLevels.Contains(e) ? e : "";
            effort.SelectedItem = effort.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == chosen);
            effort.SelectionChanged += async (_, _) =>
            {
                if (effort.SelectedItem is ComboBoxItem { Tag: string v }) await Save(x => x.Effort = v.Length == 0 ? null : v);
            };
            host.Children.Add(Card(Strings.Conn_DefaultEffort, null, effort));
        }

        var test = new Button { Content = Strings.Conn_Test };
        test.Click += async (_, _) => await LoadModels(true);
        host.Children.Add(test);
        host.Children.Add(status);
    }
}
