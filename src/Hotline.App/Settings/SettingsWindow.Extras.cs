using Hotline.App.Chat;
using Hotline.Core.Chat;
using Hotline.Core.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Settings;

/// <summary>Settings pages for the files next to settings.json: the button rows (toolbar.json) and quick actions (actions\).</summary>
public sealed partial class SettingsWindow
{
    private string DataDirectory => Path.GetDirectoryName(_settingsFile)!;
    private string ToolbarPath => Path.Combine(DataDirectory, "toolbar.json");

    /// <summary>
    /// Window › Button rows: the quadrant editor (<see cref="ToolbarQuadrantEditor"/>) on toolbar.json, read anew each time
    /// the page is shown (an edit made in the file while the page is open shows on the next visit; the bar itself follows
    /// the file at once). An unreadable file gets only the error and Reset, so nothing overwrites it by accident; the fixes
    /// Resolve made to a readable one are listed.
    /// </summary>
    private void BuildToolbarSection()
    {
        PageHost.Children.Add(new TextBlock
        {
            Text = Strings.Bar_RowsTitle, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(0, 16, 0, 4),
        });
        var load = ToolbarFile.Load(ToolbarPath);
        if (load.Error is { } error)
        {
            PageHost.Children.Add(Card(Strings.Bar_FileError, L.F(Strings.Bar_FileError_Desc, error), ResetToolbarButton()));
            return;
        }
        if (load.Model.Warnings.Count > 0)
            PageHost.Children.Add(new InfoBar
            {
                IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning,
                Message = Strings.Bar_Warnings + "\n" + string.Join("\n", load.Model.Warnings),
            });
        var actions = new QuickActions(Path.Combine(DataDirectory, "actions"));
        PageHost.Children.Add(new ToolbarQuadrantEditor(load.Model, model => ToolbarFile.Save(ToolbarPath, model),
            () => [.. actions.List().Select(a => a.Name)], ShowDialogAsync, (Style)Root.Resources["CardStyle"], _log));
        PageHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Children = { ResetToolbarButton() } });
    }

    /// <summary>
    /// Appearance › Sounds: what plays when the panel opens and closes. Off, a Windows sound (the current sound scheme)
    /// or a .wav from ~/.hotline/sounds; ▶ plays it; the folder button opens the sounds folder.
    /// </summary>
    private void BuildSoundsSection()
    {
        PageHost.Children.Add(new TextBlock
        {
            Text = Strings.Sound_Title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(0, 16, 0, 4),
        });
        var folder = Interop.SoundPlayer.Directory(DataDirectory);
        var choices = new List<(string Label, string Value)> { (Strings.Sound_Off, Hotline.Core.Windowing.Sounds.Off) };
        choices.AddRange(Interop.SoundPlayer.Files(folder).Select(f => (L.F(Strings.Sound_File, f), f)));
        choices.AddRange(Interop.SoundPlayer.WindowsEvents().Select(e => (L.F(Strings.Sound_Windows, e), Hotline.Core.Windowing.Sounds.WindowsPrefix + e)));
        UIElement Picker(Func<string> get, Action<string> set)
        {
            var box = new ComboBox { MinWidth = 260 };
            foreach (var (label, _) in choices) box.Items.Add(label);
            var initial = get();
            var current = choices.FindIndex(c => string.Equals(c.Value, initial, StringComparison.OrdinalIgnoreCase));
            if (current < 0) { box.Items.Add(initial); current = box.Items.Count - 1; } // a value typed into settings.json
            box.SelectedIndex = current;
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex >= 0 && box.SelectedIndex < choices.Count) set(choices[box.SelectedIndex].Value);
                else if (box.SelectedIndex == choices.Count) set(initial);
            };
            var play = new Button { Content = new FontIcon { Glyph = "\uE768", FontSize = 12 }, Padding = new Thickness(8, 4, 8, 4) };
            ToolTipService.SetToolTip(play, Strings.Sound_Play);
            play.Click += (_, _) => Interop.SoundPlayer.Play(get(), folder, _log);
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { box, play } };
        }
        PageHost.Children.Add(Card(Strings.Sound_Open, null, Picker(() => _settings.Current.Window.OpenSound, v => _settings.Update(s => s.Window.OpenSound = v))));
        PageHost.Children.Add(Card(Strings.Sound_Close, null, Picker(() => _settings.Current.Window.CloseSound, v => _settings.Update(s => s.Window.CloseSound = v))));
        var open = new Button { Content = Strings.Sound_OpenFolder };
        open.Click += (_, _) => { System.IO.Directory.CreateDirectory(folder); CliRunner.OpenFolder(folder); };
        PageHost.Children.Add(Card(Strings.Sound_Folder, L.F(Strings.Sound_Folder_Desc, folder), open));
    }

    /// <summary>Settings › Guide: every feature in short (Core Settings.Guide lists the sections; texts in every language).</summary>
    private void BuildGuidePage()
    {
        PageHost.Children.Add(new TextBlock { Text = Strings.Guide_Intro, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8) });
        string Text(string key) => Strings.ResourceManager.GetString(key, System.Globalization.CultureInfo.CurrentUICulture) ?? key;
        foreach (var section in Hotline.Core.Settings.Guide.Sections)
        {
            var body = new StackPanel { Spacing = 6 };
            body.Children.Add(new TextBlock
            {
                Text = Text(Hotline.Core.Settings.Guide.TitleKey(section)),
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            foreach (var paragraph in Text(Hotline.Core.Settings.Guide.BodyKey(section)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                body.Children.Add(new TextBlock { Text = paragraph, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            PageHost.Children.Add(new Border { Child = body, Style = (Style)Root.Resources["CardStyle"] });
        }
    }

    private Button ResetToolbarButton()
    {
        var reset = new Button { Content = Strings.Bar_Reset };
        reset.Click += (_, _) =>
        {
            try
            {
                Hotline.Core.Settings.FileArchive.KeepOld(ToolbarPath); // the user's file stays as toolbar.json.old (or .old2…)
                ToolbarFile.Save(ToolbarPath, ToolbarModel.Defaults);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Error("saving toolbar.json failed", ex); }
            ShowPage(_currentPage);
        };
        return reset;
    }

    /// <summary>About: version, license, where Hotline lives, and how to support it.</summary>
    private void BuildAboutPage()
    {
        string version;
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            version = $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch (InvalidOperationException) { version = Strings.About_DevBuild; } // not packaged
        PageHost.Children.Add(new TextBlock
        {
            Text = L.F(Strings.About_Intro, version),
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8),
        });
        HyperlinkButton Link(string text, Uri uri) => new() { Content = text, NavigateUri = uri, Padding = new Thickness(4, 2, 4, 2) };
        if (SupportLinks.DonationsLive)
        {
            Button Donate(string text, Uri uri, bool accent)
            {
                var button = new Button { Content = text };
                if (accent && Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) && style is Style s) button.Style = s;
                button.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(uri);
                return button;
            }
            var reminder = new ToggleSwitch { IsOn = _settings.Current.General.SupportReminder, OnContent = "", OffContent = "", MinWidth = 0 };
            reminder.Toggled += (_, _) => _settings.Update(s => s.General.SupportReminder = reminder.IsOn);
            PageHost.Children.Add(Card(Strings.About_Support, Strings.About_Support_Desc,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { Donate("GitHub Sponsors", SupportLinks.GitHubSponsors, accent: true), Donate("Ko-fi", SupportLinks.KoFi, accent: false) },
                }));
            PageHost.Children.Add(Card(Strings.About_SupportReminder, Strings.About_SupportReminder_Desc, reminder));
        }
        PageHost.Children.Add(Card(Strings.About_Project, Strings.About_Project_Desc,
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { Link("GitHub", SupportLinks.Repository), Link(Strings.About_ReportProblem, SupportLinks.Issues) },
            }));
        var report = new StackPanel { Spacing = 4 };
        report.Children.Add(new TextBlock { Text = Strings.About_Checking, Opacity = 0.7 });
        PageHost.Children.Add(new Border
        {
            Style = (Style)Root.Resources["CardStyle"],
            Child = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = Strings.About_ThisPc }, report } },
        });
        _ = FillReportAsync(report);
        PageHost.Children.Add(Card(Strings.About_License, Strings.About_License_Desc,
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { Link(Strings.About_License, SupportLinks.License), Link(Strings.About_ThirdParty, SupportLinks.ThirdPartyNotices) },
            }));
    }

    /// <summary>"This PC": which Windows features Hotline can use here, and what to do about the missing ones.</summary>
    private async Task FillReportAsync(StackPanel host)
    {
        IReadOnlyList<PcFeature> features;
        try { features = await ThisPc.CheckAsync(_odrPath); }
        catch (Exception ex) { _log.Error("this-PC report failed", ex); host.Children.Clear(); host.Children.Add(new TextBlock { Text = Strings.About_CheckFailed }); return; }
        host.Children.Clear();
        foreach (var f in features)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = f.Available ? "✓" : "✗", Opacity = f.Available ? 1 : 0.6, VerticalAlignment = VerticalAlignment.Top });
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = f.Name + "  " });
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = f.Detail, FontSize = 12 });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            host.Children.Add(row);
        }
    }

    /// <summary>Chat › memory.md: what every chat sees; opened in the editor.</summary>
    private void BuildMemoryCard()
    {
        var memory = new MemoryStore(Path.Combine(DataDirectory, "memory.md"));
        var text = memory.Read();
        var lines = text.Length == 0 ? 0 : text.Split('\n').Length;
        var edit = new Button { Content = Strings.Memory_Edit };
        edit.Click += (_, _) =>
        {
            try { memory.EnsureFile(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Error("memory.md setup failed", ex); }
            CliRunner.OpenInEditor(memory.Path);
        };
        PageHost.Children.Add(Card("memory.md", lines == 0 ? Strings.Memory_Empty : L.F(Strings.Memory_Lines, lines), edit));
    }

    /// <summary>Quick actions: "/name" in the message box applies the file's instruction (actions\&lt;name&gt;.md).</summary>
    /// <summary>
    /// Quick actions: each one's command, description and full instruction, with Edit (the .md file in your editor),
    /// Rename and Delete; New action, Restore default actions and the folder below. The files hold everything.
    /// </summary>
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
                        new TextBlock
                        {
                            Text = action.Instruction, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.65, IsTextSelectionEnabled = true,
                            Margin = new Thickness(16, -6, 16, 8), MaxLines = 6, TextTrimming = TextTrimming.CharacterEllipsis,
                        },
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
