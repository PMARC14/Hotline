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
    private readonly Dictionary<string, string> _providerIds = [];

    private IReadOnlyList<ModelFamily> Families(BackendProfile? profile) =>
        profile is not null && _loaded.TryGetValue(profile.Id, out var list) ? ModelFamilies.Group(list) : [];

    /// <summary>Model picker: clean family names ("Gemini 3.8 Flash"); the effort picker picks the variant.</summary>
    private void FillModels(BackendProfile? profile)
    {
        var families = Families(profile);
        popup.ModelBox.Items.Clear();
        popup.ModelBox.Items.Add(DefaultLabel);
        foreach (var f in families) popup.ModelBox.Items.Add(f.Name);
        var located = ModelFamilies.Locate(families, profile?.Model);
        if (!string.IsNullOrWhiteSpace(profile?.Model) && located is null) popup.ModelBox.Items.Add(profile.Model); // typed id
        popup.ModelBox.SelectedItem = string.IsNullOrWhiteSpace(profile?.Model) ? DefaultLabel : located?.Family.Name ?? profile.Model;
    }

    /// <summary>
    /// Effort picker: the same low / medium / high for every model. agy encodes the level in the model id and not
    /// every model has every level, so a choice maps to the closest level that model has (ModelFamilies.Resolve).
    /// Models without levels (e.g. Claude through agy) leave the picker disabled.
    /// </summary>
    private void FillEffort(BackendProfile? profile)
    {
        popup.EffortBox.Items.Clear();
        if (profile is null) return;
        var info = ConnectionTypes.Of(profile.Type);
        var levels = info.EffortLevels;
        if (levels.Count == 0) return;
        var located = info.EffortInModelId ? ModelFamilies.Locate(Families(profile), profile.Model) : null;
        if (located is { } hit && !hit.Family.HasLevels)
        {
            popup.EffortBox.Items.Add("n/a");
            popup.EffortBox.SelectedIndex = 0;
            return;
        }
        popup.EffortBox.Items.Add(DefaultLabel);
        foreach (var level in levels) popup.EffortBox.Items.Add(level);
        var current = located is { } h ? h.Level : profile.Effort;
        popup.EffortBox.SelectedItem = current is { Length: > 0 } c && levels.Contains(c) ? c : DefaultLabel;
    }

    private bool _updating;
    private bool _enabled = true;

    private BackendProfile? Current => settings.Current.Chat.Backends.FirstOrDefault(b => b.Id == chat.BackendId);

    public void Initialize()
    {
        popup.ProviderBox.SelectionChanged += (_, _) => { if (!_updating) OnProviderChanged(); };
        popup.ModelBox.SelectionChanged += (_, _) => { if (!_updating && popup.ModelBox.SelectedItem is string m) _ = SetModelAsync(m); };
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
            // Plain string items (so the shrink-to-fit item template applies); label → connection id.
            popup.ProviderBox.Items.Clear();
            _providerIds.Clear();
            foreach (var p in settings.Current.Chat.Backends)
            {
                var label = BackendFactory.IsAvailable(p.Type) ? p.Name : $"{p.Name} (soon)";
                while (_providerIds.ContainsKey(label)) label += " ";
                _providerIds[label] = p.Id;
                popup.ProviderBox.Items.Add(label);
            }
            popup.ProviderBox.SelectedItem = _providerIds.FirstOrDefault(kv => kv.Value == chat.BackendId).Key;

            var current = Current;
            FillModels(current);
            FillEffort(current);
            ToolTipService.SetToolTip(popup.EffortBox, "Reasoning effort");
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
        var effortAvailable = popup.EffortBox.Items.Count > 0;
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
        if (popup.ProviderBox.SelectedItem is not string label || !_providerIds.TryGetValue(label, out var id) || id == chat.BackendId) return;
        if (settings.Current.Chat.Backends.FirstOrDefault(b => b.Id == id) is { } target && !BackendFactory.IsAvailable(target.Type))
        {
            notify($"{target.Name} can be set up in Settings, but chatting with it arrives in a later update.", InfoBarSeverity.Informational);
            popup.DispatcherQueue.TryEnqueue(Refresh); // back to the current connection, after this SelectionChanged
            return;
        }
        chat.BackendId = id;
        settings.Update(s => s.Chat.DefaultBackend = id); // Changed → Refresh
        _ = LoadModelsAsync(quiet: true);
        log.Info($"provider switched to {id}");
    }

    private async Task SetModelAsync(string text)
    {
        var profile = Current;
        if (profile is null) return;
        var families = Families(profile);
        string? model; string? effort = profile.Effort;
        if (string.IsNullOrWhiteSpace(text) || text == DefaultLabel) model = null;
        else if (families.FirstOrDefault(f => f.Name == text) is { } plain && !ConnectionTypes.Of(profile.Type).EffortInModelId)
            model = plain.Variants[0].Id; // effort is a separate setting (e.g. Claude Code --effort)
        else if (families.FirstOrDefault(f => f.Name == text) is { } family)
        {
            // Keep the current effort level when switching models (nearest level the new model has).
            var level = ModelFamilies.Locate(families, profile.Model)?.Level is { Length: > 0 } l ? l : profile.Effort;
            model = ModelFamilies.Resolve(family, level);
            effort = null; // the level is part of the model id
        }
        else model = text.Trim(); // a typed model id
        if (model == profile.Model && effort == profile.Effort) return;
        settings.Update(_ => { profile.Model = model; profile.Effort = effort; });
        await invalidate(profile.Id);
        log.Info($"model for {profile.Id} = {model ?? "default"}");
    }

    private async Task SetEffortAsync(string level)
    {
        var profile = Current;
        if (profile is null) return;
        if (level == "n/a") return;
        if (ConnectionTypes.Of(profile.Type).EffortInModelId && ModelFamilies.Locate(Families(profile), profile.Model) is { } hit)
        {
            var model = ModelFamilies.Resolve(hit.Family, level == DefaultLabel ? null : level);
            if (model == profile.Model) return;
            settings.Update(_ => profile.Model = model);
        }
        else
        {
            var effort = level == DefaultLabel ? null : level;
            if (effort == profile.Effort) return;
            settings.Update(_ => profile.Effort = effort);
        }
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
            // Same list as shown already (the usual case when the dropdown opens): leave the open dropdown alone.
            if (_loaded.TryGetValue(profile.Id, out var shown) && shown.SequenceEqual(list)) return;
            _loaded[profile.Id] = list;
            if (Current != profile) return; // switched meanwhile
            _updating = true;
            FillModels(profile);
            FillEffort(profile);
            SetEnabled(_enabled); // levels just arrived: enable the effort picker
        }
        catch (ModelListException ex) { log.Info($"model list: {ex.Message}"); if (!quiet) notify(ex.Message, InfoBarSeverity.Warning); }
        catch (Exception ex) { log.Error("model list failed", ex); if (!quiet) notify($"Couldn't list models: {ex.Message}", InfoBarSeverity.Warning); }
        finally { _updating = false; }
        Layout();
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
