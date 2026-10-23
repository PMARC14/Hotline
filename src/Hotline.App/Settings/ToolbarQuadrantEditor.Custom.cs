using Hotline.Core.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Settings;

/// <summary>The custom buttons list: add, edit and delete (the dialog is <see cref="ToolbarCustomButtonDialog"/>). A button's
/// picture file (icons\&lt;id&gt;.png) is deleted with the button, or when it goes back to a built-in icon.</summary>
internal sealed partial class ToolbarQuadrantEditor
{
    private readonly StackPanel _custom = new() { Spacing = 6 };

    private void BuildCustomList()
    {
        _custom.Children.Clear();
        if (_model.Buttons.Count == 0) _custom.Children.Add(new TextBlock { Text = Strings.Bar_CustomNone, Opacity = 0.7 });
        foreach (var b in _model.Buttons)
        {
            var edit = new Button { Content = Strings.Common_Edit };
            var delete = new Button { Content = Strings.Common_Delete };
            Automation(edit, "bar-custom-edit-" + b.Id, $"{Strings.Common_Edit}: {b.Label}");
            Automation(delete, "bar-custom-delete-" + b.Id, $"{Strings.Common_Delete}: {b.Label}");
            edit.Click += (_, _) => _ = EditCustomAsync(b);
            delete.Click += (_, _) => _ = DeleteCustomAsync(b);
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = b.Label, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock
            {
                FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis,
                Text = b.QuickAction is { } action ? L.F(Strings.Bar_CustomRuns, action) : b.Send ? Strings.Bar_CustomSends : Strings.Bar_CustomInserts,
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { edit, delete } };
            Grid.SetColumn(text, 1);
            Grid.SetColumn(buttons, 2);
            row.Children.Add(PictureOrGlyph(b));
            row.Children.Add(text);
            row.Children.Add(buttons);
            _custom.Children.Add(new Border { Style = _cardStyle, Child = row });
        }
        var add = new Button { Content = Strings.Bar_CustomAdd };
        Automation(add, "bar-custom-add", Strings.Bar_CustomAdd);
        add.Click += (_, _) => _ = EditCustomAsync(null);
        _custom.Children.Add(add);
    }

    /// <summary>A button's picture, or its glyph when it has none or the picture can't be drawn.</summary>
    private FrameworkElement PictureOrGlyph(CustomButton b)
    {
        FontIcon Glyph() => new() { Glyph = IconGlyph(b.Icon), FontSize = 16 };
        var slot = new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        if (CustomButtonPicture.Read(b, _log.Info) is { } picture)
            slot.Children.Add(CustomButtonPicture.View(picture, 20, () => { slot.Children.Clear(); slot.Children.Add(Glyph()); }));
        else slot.Children.Add(Glyph());
        return slot;
    }

    /// <summary>Deletes a button's picture file once the saved model no longer uses it (the button was deleted, or went back
    /// to a built-in icon). Not when saving failed: the file on disk still names it.</summary>
    private void ForgetPicture(string id)
    {
        if (_model.Buttons.Any(b => b.Id == id && b.IconFile is not null)) return;
        if (CustomIcons.Delete(CustomButtonPicture.Folder, id)) _log.Info($"button rows: deleted the picture of {id}");
    }

    /// <summary>Adds (<paramref name="existing"/> null) or edits a custom button. Adding with no room says so at once.</summary>
    private async Task EditCustomAsync(CustomButton? existing)
    {
        try
        {
            if (existing is null)
            {
                // Any valid button: only the room checks can fail.
                var room = ToolbarEditorView.Check(_model, new CustomButton("x", "x", ToolbarModel.DefaultIcon, null, "x", false), adding: true);
                if (room != CustomProblem.None) { Notice(ToolbarCustomButtonDialog.Message(room), InfoBarSeverity.Warning); return; }
            }
            var (next, pictureReplaced) = await ToolbarCustomButtonDialog.ShowAsync(_model, existing, _quickActions(), _showDialog, XamlRoot, _log.Info);
            var focus = existing is null ? "bar-custom-add" : "bar-custom-edit-" + existing.Id;
            if (next is not null) Edit(_ => next, existing?.Id, (focus, "bar-custom-add"));
            else if (pictureReplaced) { _focus = (focus, "bar-custom-add"); QueueRebuild(); } // same model, new picture: redraw the list
            if (existing?.IconFile is not null) ForgetPicture(existing.Id);
        }
        catch (Exception ex) { _log.Error("button rows: custom button dialog failed", ex); }
    }

    private async Task DeleteCustomAsync(CustomButton b)
    {
        try
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = L.F(Strings.Bar_CustomDeleteConfirm, b.Label),
                PrimaryButtonText = Strings.Common_Delete, CloseButtonText = Strings.Common_Cancel, DefaultButton = ContentDialogButton.Close,
            };
            if (await _showDialog(confirm) != ContentDialogResult.Primary) return;
            Edit(m => ToolbarEditor.RemoveCustom(m, b.Id), null, ("bar-custom-add", "bar-custom-add"));
            _stash.Remove(b.Id);
            if (!_model.Buttons.Any(x => x.Id == b.Id)) ForgetPicture(b.Id); // only once the deletion is saved
        }
        catch (Exception ex) { _log.Error("button rows: deleting a custom button failed", ex); }
    }
}
