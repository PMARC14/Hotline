using Hotline.Core.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Settings;

/// <summary>
/// Adding or editing one custom button: label, icon (from <see cref="ToolbarEditorView.Icons"/>), and either a quick action
/// (a file in actions\) or text to insert (and maybe send). The id is made from the label when adding and never changes.
/// Save is refused, with the reason, until <see cref="ToolbarEditor"/> takes the button.
/// </summary>
internal static class ToolbarCustomButtonDialog
{
    public static string Message(CustomProblem problem) => problem switch
    {
        CustomProblem.NoLabel => Strings.Bar_CustomNeedsLabel,
        CustomProblem.NoAction => Strings.Bar_CustomNeedsAction,
        CustomProblem.BadQuickAction => Strings.Bar_CustomBadAction,
        CustomProblem.TooManyButtons => Strings.Bar_CustomTooMany,
        CustomProblem.GroupFull => Strings.Bar_CustomGroupFull,
        _ => Strings.Bar_CustomRefused,
    };

    /// <summary>The model with the button added or changed, or null when cancelled or nothing changed.</summary>
    public static async Task<ToolbarModel?> ShowAsync(ToolbarModel model, CustomButton? existing, IReadOnlyList<string> actionFiles,
        Func<ContentDialog, Task<ContentDialogResult>> show, XamlRoot root)
    {
        var adding = existing is null;
        var label = new TextBox { Header = Strings.Bar_CustomLabel, Text = existing?.Label ?? "", MaxLength = 40 };
        var id = new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        string Id() => existing?.Id ?? ToolbarEditorView.NewCustomId(model, label.Text);
        label.TextChanged += (_, _) => id.Text = L.F(Strings.Bar_CustomId, Id());
        id.Text = L.F(Strings.Bar_CustomId, Id());

        var icons = ToolbarEditorView.Icons.ToList();
        if (existing is not null && !icons.Contains(existing.Icon)) icons.Add(existing.Icon); // set by hand in the file
        var iconGrid = new GridView { Header = Strings.Bar_CustomIcon, SelectionMode = ListViewSelectionMode.Single };
        foreach (var icon in icons)
        {
            var item = new GridViewItem { Content = new FontIcon { Glyph = ToolbarQuadrantEditor.IconGlyph(icon), FontSize = 18 }, Tag = icon, Width = 44, Height = 44 };
            AutomationProperties.SetName(item, icon);
            iconGrid.Items.Add(item);
        }
        iconGrid.SelectedIndex = Math.Max(0, icons.IndexOf(existing?.Icon ?? ToolbarModel.DefaultIcon));

        var choices = ToolbarEditorView.ActionChoices(actionFiles, existing?.QuickAction);
        var runAction = new RadioButton { Content = Strings.Bar_CustomQuickAction, GroupName = "action" };
        var insertText = new RadioButton { Content = Strings.Bar_CustomText, GroupName = "action" };
        var actions = new ComboBox { MinWidth = 240, PlaceholderText = choices.Count == 0 ? Strings.Bar_CustomNoActions : "" };
        foreach (var name in choices) actions.Items.Add(name);
        actions.SelectedItem = choices.FirstOrDefault(c => string.Equals(c, existing?.QuickAction, StringComparison.OrdinalIgnoreCase));
        var text = new TextBox { Text = existing?.Text ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 2000, Height = 80 };
        var send = new CheckBox { Content = Strings.Bar_CustomSend, IsChecked = existing?.Send == true };
        foreach (var (element, automationId, name) in new (UIElement, string, string)[]
        {
            (label, "bar-custom-label", Strings.Bar_CustomLabel), (iconGrid, "bar-custom-icon", Strings.Bar_CustomIcon),
            (runAction, "bar-custom-quickaction", Strings.Bar_CustomQuickAction), (actions, "bar-custom-actions", Strings.Bar_CustomQuickAction),
            (insertText, "bar-custom-text", Strings.Bar_CustomText), (text, "bar-custom-textbox", Strings.Bar_CustomText),
            (send, "bar-custom-send", Strings.Bar_CustomSend),
        })
        {
            AutomationProperties.SetAutomationId(element, automationId);
            AutomationProperties.SetName(element, name);
        }
        void Sync()
        {
            actions.IsEnabled = runAction.IsChecked == true;
            text.IsEnabled = send.IsEnabled = insertText.IsChecked == true;
        }
        runAction.Checked += (_, _) => Sync();
        insertText.Checked += (_, _) => Sync();
        // A new button runs a quick action when there are any; an existing one keeps its kind.
        var useAction = existing is null ? choices.Count > 0 : existing.QuickAction is not null;
        (useAction ? runAction : insertText).IsChecked = true;
        Sync();

        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false, IsOpen = false };
        ToolbarModel? result = null;
        var dialog = new ContentDialog
        {
            XamlRoot = root, Title = adding ? Strings.Bar_CustomNewTitle : Strings.Bar_CustomEditTitle,
            Content = new ScrollViewer
            {
                Content = new StackPanel { Spacing = 8, MinWidth = 380, Children = { label, id, iconGrid, runAction, actions, insertText, text, send, error } },
            },
            PrimaryButtonText = Strings.Bar_CustomSave, CloseButtonText = Strings.Common_Cancel, DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            var quick = runAction.IsChecked == true;
            var button = new CustomButton(
                Id(), label.Text.Trim(),
                iconGrid.SelectedItem is FrameworkElement { Tag: string icon } ? icon : ToolbarModel.DefaultIcon,
                quick ? actions.SelectedItem as string ?? "" : null,
                quick ? null : text.Text.Replace("\r\n", "\n").Replace('\r', '\n'), // a WinUI TextBox ends lines with \r
                !quick && send.IsChecked == true);
            if (button == existing) return; // nothing changed
            var problem = ToolbarEditorView.Check(model, button, adding);
            var next = problem != CustomProblem.None ? model : adding ? ToolbarEditor.AddCustom(model, button) : ToolbarEditor.UpdateCustom(model, button);
            if (!ReferenceEquals(next, model)) { result = next; return; }
            error.Message = Message(problem);
            error.IsOpen = true;
            e.Cancel = true; // keep the dialog open
        };
        return await show(dialog) == ContentDialogResult.Primary ? result : null;
    }
}
