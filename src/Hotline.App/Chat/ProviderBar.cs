using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Chat;

/// <summary>Bottom-bar pickers: provider (connection), model and effort, plus the system-prompt menu.</summary>
internal sealed class ProviderBar(
    PopupWindow popup, SettingsService settings, ChatController chat, Func<string, ValueTask> invalidate, ModelCatalog models,
    PromptLibrary prompts, Action<string, InfoBarSeverity> notify, FileLog log)
{
    private const string DefaultLabel = "Default";
    private readonly Dictionary<string, IReadOnlyList<ModelInfo>> _loaded = [];

    private void FillModels(BackendProfile? profile, IReadOnlyList<ModelInfo> list)
    {
        popup.ModelBox.Items.Clear();
        popup.ModelBox.Items.Add(DefaultLabel);
        foreach (var m in list) popup.ModelBox.Items.Add(m.Id);
        if (!string.IsNullOrWhiteSpace(profile?.Model) && !list.Any(m => m.Id == profile.Model)) popup.ModelBox.Items.Add(profile.Model);
    }
    private bool _updating;
    private bool _enabled = true;

    private BackendProfile? Current => settings.Current.Chat.Backends.FirstOrDefault(b => b.Id == chat.BackendId);

    public void Initialize()
    {
        popup.ProviderBox.SelectionChanged += (_, _) => { if (!_updating) OnProviderChanged(); };
        popup.ModelBox.SelectionChanged += (_, _) => { if (!_updating && popup.ModelBox.SelectedItem is string m) _ = SetModelAsync(m); };
        popup.ModelBox.TextSubmitted += (sender, e) => { if (!_updating) _ = SetModelAsync(e.Text); };
        popup.ModelBox.DropDownOpened += (_, _) => _ = LoadModelsAsync();
        popup.EffortBox.SelectionChanged += (_, _) => { if (!_updating && popup.EffortBox.SelectedItem is string e) _ = SetEffortAsync(e); };
        popup.PromptMenu.Opening += (_, _) => BuildPromptMenu();
        popup.Toolbar.SizeChanged += (_, _) => Layout();
        Refresh();
        _ = LoadModelsAsync(quiet: true); // preload so the dropdown is complete when first opened
    }

    /// <summary>Disabled while an answer streams: changing model/effort restarts the backend.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        popup.ProviderBox.IsEnabled = popup.ModelBox.IsEnabled = enabled;
        popup.EffortBox.IsEnabled = enabled && popup.EffortBox.Items.Count > 1;
    }

    public void Refresh()
    {
        _updating = true;
        try
        {
            popup.ProviderBox.Items.Clear();
            foreach (var p in settings.Current.Chat.Backends)
            {
                var available = BackendFactory.IsAvailable(p.Type);
                popup.ProviderBox.Items.Add(new ComboBoxItem { Content = available ? p.Name : $"{p.Name} (coming soon)", Tag = p.Id, IsEnabled = available });
            }
            popup.ProviderBox.SelectedItem = popup.ProviderBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == chat.BackendId);

            var current = Current;
            FillModels(current, current is not null && _loaded.TryGetValue(current.Id, out var known) ? known : []);
            popup.ModelBox.SelectedItem = string.IsNullOrWhiteSpace(current?.Model) ? DefaultLabel : current.Model;

            IReadOnlyList<string> levels = current is null ? Array.Empty<string>() : ConnectionTypes.Of(current.Type).EffortLevels;
            popup.EffortBox.Items.Clear();
            popup.EffortBox.Items.Add(DefaultLabel);
            foreach (var level in levels) popup.EffortBox.Items.Add(level);
            popup.EffortBox.SelectedItem = current?.Effort is { } e && levels.Contains(e) ? e : DefaultLabel;
            ToolTipService.SetToolTip(popup.EffortBox, levels.Count > 0 ? "Reasoning effort" : "This provider doesn't offer effort levels yet");
            SetEnabled(_enabled);
        }
        finally { _updating = false; }
        Layout();
    }

    private static readonly string[] HideOrder = ["effort", "provider", "model"];

    /// <summary>
    /// Gives the pickers explicit widths from the space the fixed buttons leave (see ToolbarLayout): the bar never
    /// reflows when a picker's text changes and the buttons on the right never get pushed off the edge.
    /// </summary>
    private void Layout()
    {
        var bar = popup.Toolbar;
        if (bar.ActualWidth <= 0) return;
        var fixedWidth = bar.Children.OfType<FrameworkElement>().Where(c => c != popup.PickersPanel).Sum(c => c.ActualWidth + c.Margin.Left + c.Margin.Right)
                         + bar.ColumnSpacing * (bar.ColumnDefinitions.Count - 1);
        var effortAvailable = popup.EffortBox.Items.Count > 1;
        var widths = ToolbarLayout.Compute(bar.ActualWidth, fixedWidth, popup.PickersPanel.Spacing,
            [new("effort", 72, 56, effortAvailable), new("model", 150, 84), new("provider", 128, 72)], HideOrder);
        Apply(popup.EffortBox, widths["effort"]);
        Apply(popup.ModelBox, widths["model"]);
        Apply(popup.ProviderBox, widths["provider"]);

        static void Apply(ComboBox box, double width)
        {
            box.Visibility = width > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (width > 0) box.Width = width;
        }
    }

    private void OnProviderChanged()
    {
        if (popup.ProviderBox.SelectedItem is not ComboBoxItem { Tag: string id } || id == chat.BackendId) return;
        chat.BackendId = id;
        settings.Update(s => s.Chat.DefaultBackend = id); // Changed → Refresh
        _ = LoadModelsAsync(quiet: true);
        log.Info($"provider switched to {id}");
    }

    private async Task SetModelAsync(string text)
    {
        var profile = Current;
        if (profile is null) return;
        var model = string.IsNullOrWhiteSpace(text) || text == DefaultLabel ? null : text.Trim();
        if (model == profile.Model) return;
        settings.Update(_ => profile.Model = model);
        await invalidate(profile.Id);
        log.Info($"model for {profile.Id} = {model ?? "default"}");
    }

    private async Task SetEffortAsync(string level)
    {
        var profile = Current;
        if (profile is null) return;
        var effort = level == DefaultLabel ? null : level;
        if (effort == profile.Effort) return;
        settings.Update(_ => profile.Effort = effort);
        await invalidate(profile.Id);
    }

    /// <summary>Fills the model list (cached per connection). Quiet = background preload: problems are only logged.</summary>
    private async Task LoadModelsAsync(bool quiet = false)
    {
        var profile = Current;
        if (profile is null) return;
        try
        {
            var list = await models.GetAsync(profile, refresh: false, CancellationToken.None);
            _loaded[profile.Id] = list;
            if (Current != profile) return; // switched meanwhile
            _updating = true;
            var selected = popup.ModelBox.SelectedItem as string ?? DefaultLabel;
            FillModels(profile, list);
            popup.ModelBox.SelectedItem = popup.ModelBox.Items.Contains(selected) ? selected : DefaultLabel;
        }
        catch (ModelListException ex) { log.Info($"model list: {ex.Message}"); if (!quiet) notify(ex.Message, InfoBarSeverity.Warning); }
        catch (Exception ex) { log.Error("model list failed", ex); if (!quiet) notify($"Couldn't list models: {ex.Message}", InfoBarSeverity.Warning); }
        finally { _updating = false; }
    }

    private void BuildPromptMenu()
    {
        popup.PromptMenu.Items.Clear();
        var profile = Current;
        var active = profile?.Prompt ?? settings.Current.Chat.DefaultPrompt;
        foreach (var name in prompts.List())
        {
            var item = new ToggleMenuFlyoutItem { Text = name, IsChecked = name == active };
            item.Click += async (_, _) =>
            {
                if (profile is null) return;
                settings.Update(_ => profile.Prompt = name == settings.Current.Chat.DefaultPrompt ? null : name);
                await invalidate(profile.Id);
                notify($"System prompt: {name} (applies to the next message)", InfoBarSeverity.Informational);
            };
            popup.PromptMenu.Items.Add(item);
        }
        popup.PromptMenu.Items.Add(new MenuFlyoutSeparator());
        var edit = new MenuFlyoutItem { Text = $"Edit \"{active}\"…" };
        edit.Click += (_, _) => { popup.HidePopup(); CliRunner.OpenInEditor(prompts.PathFor(active)); };
        var create = new MenuFlyoutItem { Text = "New prompt…" };
        create.Click += (_, _) => { popup.HidePopup(); CliRunner.OpenInEditor(prompts.PathFor(prompts.Create())); };
        var folder = new MenuFlyoutItem { Text = "Open prompts folder" };
        folder.Click += (_, _) => { popup.HidePopup(); CliRunner.OpenFolder(prompts.Directory); };
        popup.PromptMenu.Items.Add(edit);
        popup.PromptMenu.Items.Add(create);
        popup.PromptMenu.Items.Add(folder);
    }
}
