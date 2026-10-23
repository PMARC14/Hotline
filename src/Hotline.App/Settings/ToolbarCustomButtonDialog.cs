using Hotline.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Hotline.App.Settings;

/// <summary>
/// Adding or editing one custom button: label, icon (from <see cref="ToolbarEditorView.Icons"/>, or a PNG of the user's own
/// copied to icons\ on save, see <see cref="CustomIcons"/>), and either a quick action
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

    public static string Message(CustomIconProblem problem) => problem switch
    {
        CustomIconProblem.NotPng => Strings.Bar_IconNotPng,
        CustomIconProblem.TooLarge => Strings.Bar_IconTooLarge,
        CustomIconProblem.TooSmall or CustomIconProblem.TooBig => Strings.Bar_IconSize,
        CustomIconProblem.Corrupt => Strings.Bar_IconCorrupt,
        _ => Strings.Bar_IconUnreadable,
    };

    /// <summary>The model with the button added or changed (null when cancelled or nothing in it changed), and whether the
    /// button's picture file was replaced (its name stays the same, so the model alone doesn't show that).</summary>
    public static async Task<(ToolbarModel? Model, bool PictureReplaced)> ShowAsync(ToolbarModel model, CustomButton? existing,
        IReadOnlyList<string> actionFiles, Func<ContentDialog, Task<ContentDialogResult>> show, XamlRoot root, Action<string> log)
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

        // A picture instead of the glyph (which stays as the fallback). The chosen file is only checked here; it is copied
        // to icons\ when the dialog is saved, so cancelling leaves nothing behind.
        string? picked = null; // a checked file to copy on save
        var cleared = false;    // "use a built-in icon instead" on a button that has a picture
        var pictureHint = new TextBlock { Text = Strings.Bar_CustomPictureHint, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        var choosePicture = new Button { Content = Strings.Bar_CustomPicture };
        var clearPicture = new Button { Content = Strings.Bar_CustomPictureClear };
        var preview = new Grid { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center };
        var pictureError = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false, IsOpen = false };
        void ShowPicture(byte[]? png)
        {
            preview.Children.Clear();
            if (png is not null) preview.Children.Add(CustomButtonPicture.View(png, 32, () => preview.Children.Clear()));
            preview.Visibility = png is null ? Visibility.Collapsed : Visibility.Visible;
            // Also when the stored picture is gone: the button can still drop it.
            var hasPicture = picked is not null || (!cleared && existing?.IconFile is not null);
            clearPicture.Visibility = hasPicture ? Visibility.Visible : Visibility.Collapsed;
        }
        ShowPicture(existing is null ? null : CustomButtonPicture.Read(existing, log));
        choosePicture.Click += async (_, _) =>
        {
            // An exception escaping this async handler would end the process.
            try
            {
                var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail, SuggestedStartLocation = PickerLocationId.PicturesLibrary };
                picker.FileTypeFilter.Add(".png");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId));
                if (await picker.PickSingleFileAsync() is not { } file) return;
                var (png, problem) = CustomIcons.Read(file.Path); // a file with no local path (a cloud item) can't be read
                pictureError.IsOpen = png is null;
                if (png is null) { pictureError.Message = Message(problem); return; }
                (picked, cleared) = (file.Path, false);
                ShowPicture(png);
            }
            catch (Exception ex)
            {
                log($"button rows: choosing a picture failed ({ex.GetType().Name})"); // no message: it can hold the path
                pictureError.Message = Message(CustomIconProblem.Unreadable);
                pictureError.IsOpen = true;
            }
        };
        clearPicture.Click += (_, _) =>
        {
            (picked, cleared, pictureError.IsOpen) = (null, true, false);
            ShowPicture(null);
        };
        var pictureRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { choosePicture, preview, clearPicture } };

        var choices = ToolbarEditorView.ActionChoices(actionFiles, existing?.QuickAction);
        var runAction = new RadioButton { Content = Strings.Bar_CustomQuickAction, GroupName = "action" };
        var insertText = new RadioButton { Content = Strings.Bar_CustomText, GroupName = "action" };
        var actions = new ComboBox { MinWidth = 240, PlaceholderText = choices.Count == 0 ? Strings.Bar_CustomNoActions : "" };
        foreach (var name in choices) actions.Items.Add(name);
        actions.SelectedItem = existing?.QuickAction is null
            ? choices.FirstOrDefault() // adding: "run a quick action" is the default, so it starts with one chosen
            : choices.FirstOrDefault(c => string.Equals(c, existing.QuickAction, StringComparison.OrdinalIgnoreCase));
        var text = new TextBox { Text = existing?.Text ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 2000, Height = 80 };
        var send = new CheckBox { Content = Strings.Bar_CustomSend, IsChecked = existing?.Send == true };
        foreach (var (element, automationId, name) in new (UIElement, string, string)[]
        {
            (label, "bar-custom-label", Strings.Bar_CustomLabel), (iconGrid, "bar-custom-icon", Strings.Bar_CustomIcon),
            (runAction, "bar-custom-quickaction", Strings.Bar_CustomQuickAction), (actions, "bar-custom-actions", Strings.Bar_CustomQuickAction),
            (insertText, "bar-custom-text", Strings.Bar_CustomText), (text, "bar-custom-textbox", Strings.Bar_CustomText),
            (send, "bar-custom-send", Strings.Bar_CustomSend), (choosePicture, "bar-custom-picture", Strings.Bar_CustomPicture),
            (clearPicture, "bar-custom-picture-clear", Strings.Bar_CustomPictureClear), (preview, "bar-custom-picture-preview", Strings.Bar_CustomPicturePreview),
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
        var pictureReplaced = false;
        var dialog = new ContentDialog
        {
            XamlRoot = root, Title = adding ? Strings.Bar_CustomNewTitle : Strings.Bar_CustomEditTitle,
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Spacing = 8, MinWidth = 380,
                    Children = { label, id, iconGrid, pictureHint, pictureRow, pictureError, runAction, actions, insertText, text, send, error },
                },
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
                !quick && send.IsChecked == true,
                picked is not null ? Id() + ".png" : cleared ? null : existing?.IconFile);
            // A new picture for a button that already had one keeps the same name: only the file changes.
            var unchanged = button == existing;
            if (unchanged && picked is null) return; // nothing changed
            var problem = ToolbarEditorView.Check(model, button, adding);
            var next = problem != CustomProblem.None || unchanged ? model
                : adding ? ToolbarEditor.AddCustom(model, button) : ToolbarEditor.UpdateCustom(model, button);
            if (problem == CustomProblem.None && (unchanged || !ReferenceEquals(next, model)))
            {
                if (picked is not null)
                {
                    // Copied only now, after the checks: a refused or cancelled button leaves no file behind.
                    var (stored, iconProblem) = CustomIcons.Import(picked, CustomButtonPicture.Folder, button.Id);
                    if (stored is null)
                    {
                        log($"button rows: storing the picture of {button.Id} failed ({iconProblem})");
                        (pictureError.Message, pictureError.IsOpen, e.Cancel) = (Message(iconProblem), true, true);
                        return;
                    }
                    pictureReplaced = true;
                }
                result = unchanged ? null : next;
                return;
            }
            error.Message = Message(problem);
            error.IsOpen = true;
            e.Cancel = true; // keep the dialog open
        };
        return await show(dialog) == ContentDialogResult.Primary ? (result, pictureReplaced) : (null, false);
    }
}
