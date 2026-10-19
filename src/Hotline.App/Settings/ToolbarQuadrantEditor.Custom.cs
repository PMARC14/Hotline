using Hotline.Core.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Settings;

/// <summary>The custom buttons list: add, edit and delete (the dialog is <see cref="ToolbarCustomButtonDialog"/>).</summary>
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
            row.Children.Add(new FontIcon { Glyph = IconGlyph(b.Icon), FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(text);
            row.Children.Add(buttons);
            _custom.Children.Add(new Border { Style = _cardStyle, Child = row });
        }
        var add = new Button { Content = Strings.Bar_CustomAdd };
        Automation(add, "bar-custom-add", Strings.Bar_CustomAdd);
        add.Click += (_, _) => _ = EditCustomAsync(null);
        _custom.Children.Add(add);
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
            var next = await ToolbarCustomButtonDialog.ShowAsync(_model, existing, _quickActions(), _showDialog, XamlRoot);
            var focus = existing is null ? "bar-custom-add" : "bar-custom-edit-" + existing.Id;
            if (next is not null) Edit(_ => next, existing?.Id, (focus, "bar-custom-add"));
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
        }
        catch (Exception ex) { _log.Error("button rows: deleting a custom button failed", ex); }
    }
}
