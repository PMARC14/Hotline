using Hotline.App.Chat;
using Hotline.Core.Chat;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Settings;

/// <summary>Settings › Actions: the quick actions (actions\ folder).</summary>
public sealed partial class SettingsWindow
{
    private void BuildActionsPage()
    {
        var actions = new QuickActions(Path.Combine(DataDirectory, "actions"));
        PageHost.Children.Add(new TextBlock
        {
            Text = Strings.Actions_Intro,
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8),
        });
        foreach (var action in actions.List())
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var edit = new Button { Content = Strings.Common_Edit };
            edit.Click += (_, _) => CliRunner.OpenInEditor(actions.PathFor(action.Name));
            var rename = new Button { Content = Strings.Actions_Rename };
            rename.Click += async (_, _) => await RenameActionAsync(actions, action.Name);
            var delete = new Button { Content = Strings.Common_Delete };
            delete.Click += async (_, _) =>
            {
                var confirm = new ContentDialog
                {
                    XamlRoot = Root.XamlRoot, Title = L.F(Strings.Actions_DeleteConfirm, action.Name),
                    PrimaryButtonText = Strings.Common_Delete, CloseButtonText = Strings.Common_Cancel, DefaultButton = ContentDialogButton.Close,
                };
                if (await ShowDialogAsync(confirm) != ContentDialogResult.Primary) return;
                actions.Delete(action.Name);
                ShowPage("Actions");
            };
            buttons.Children.Add(edit);
            buttons.Children.Add(rename);
            buttons.Children.Add(delete);
            var description = new TextBlock { Text = action.Description };
            var card = CardWithStatus("/" + action.Name, description, buttons);
            // The whole instruction under the card, when it says more than the description.
            if (action.Instruction != action.Description)
                PageHost.Children.Add(new StackPanel
                {
                    Spacing = 0,
                    Children =
                    {
                        card,
                        InstructionPreview(action.Instruction),
                    },
                });
            else PageHost.Children.Add(card);
        }
        var create = new Button { Content = Strings.Actions_New, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        create.Click += (_, _) => { CliRunner.OpenInEditor(actions.PathFor(actions.Create())); ShowPage("Actions"); };
        var restore = new Button { Content = Strings.Actions_Restore };
        restore.Click += (_, _) =>
        {
            var written = actions.RestoreDefaults();
            ShowPage("Actions");
            PageHost.Children.Insert(1, new InfoBar
            {
                IsOpen = true, IsClosable = true, Severity = InfoBarSeverity.Success,
                Message = L.F(Strings.Actions_Restored, written), Margin = new Thickness(0, 0, 0, 8),
            });
        };
        var open = new Button { Content = Strings.Actions_OpenFolder };
        open.Click += (_, _) => { Directory.CreateDirectory(actions.Directory); CliRunner.OpenFolder(actions.Directory); };
        PageHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { create, restore, open } });
    }

    /// <summary>Asks for a new name (letters, digits, - and _, not taken) and renames the action's file.</summary>
    private async Task RenameActionAsync(QuickActions actions, string name)
    {
        var box = new TextBox { Text = name, Header = Strings.Actions_NewName };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = Strings.Actions_Rename, Content = box,
            PrimaryButtonText = Strings.Actions_Rename, CloseButtonText = Strings.Common_Cancel, DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            var to = box.Text.Trim();
            if (to == name) return;
            if (actions.Rename(name, to)) return;
            box.Header = Strings.Actions_RenameInvalid; // keep the dialog open
            e.Cancel = true;
        };
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary) ShowPage("Actions");
    }
}
