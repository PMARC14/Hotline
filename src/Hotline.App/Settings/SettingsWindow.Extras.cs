using Hotline.App.Chat;
using Hotline.Core.Chat;
using Hotline.Core.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Settings;

/// <summary>Settings pages for the files next to settings.json: the bottom bar (toolbar.json) and quick actions (actions\).</summary>
public sealed partial class SettingsWindow
{
    /// <summary>A bar item's name ("captureWindow" → Bar_captureWindow); unknown ids show as they are.</summary>
    private static string ToolbarName(string id) => Strings.ResourceManager.GetString("Bar_" + id, System.Globalization.CultureInfo.CurrentUICulture) ?? id;

    private string DataDirectory => Path.GetDirectoryName(_settingsFile)!;
    private string ToolbarPath => Path.Combine(DataDirectory, "toolbar.json");

    /// <summary>Appearance › Bottom bar: show, hide and order the bar's buttons (saved to toolbar.json, applied at once).</summary>
    private void BuildToolbarSection()
    {
        PageHost.Children.Add(new TextBlock
        {
            Text = Strings.Bar_Title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(0, 16, 0, 4),
        });
        var config = ToolbarConfig.Load(ToolbarPath);
        if (config.Error is { } error)
        {
            PageHost.Children.Add(Card(Strings.Bar_FileError, L.F(Strings.Bar_FileError_Desc, error), ResetToolbarButton()));
            return;
        }
        var items = config.Items;
        void Save(IReadOnlyList<string> next)
        {
            try { ToolbarConfig.Save(ToolbarPath, next); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Error("saving toolbar.json failed", ex); }
            ShowPage(_currentPage);
        }
        var list = new StackPanel { Spacing = 4 };
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var c = 0; c < 3; c++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = ToolbarName(items[i]), VerticalAlignment = VerticalAlignment.Center });
            AddIconButton(row, 1, "\uE76B", Strings.Bar_MoveLeft, index > 0, () => Save(ToolbarConfig.Move(items, index, -1)));
            AddIconButton(row, 2, "\uE76C", Strings.Bar_MoveRight, index < items.Count - 1, () => Save(ToolbarConfig.Move(items, index, +1)));
            AddIconButton(row, 3, "\uE711", Strings.Bar_Remove, items.Count > 1, () =>
                Save(items.Where((_, j) => j != index).ToList()));
            list.Children.Add(row);
        }
        PageHost.Children.Add(new Border { Child = list, Style = (Style)Root.Resources["CardStyle"] });

        var add = new DropDownButton { Content = Strings.Bar_Add };
        var menu = new MenuFlyout();
        foreach (var item in ToolbarConfig.Available.Where(a => a == "spacer" || !items.Contains(a)))
        {
            var entry = new MenuFlyoutItem { Text = ToolbarName(item) };
            entry.Click += (_, _) => Save(ToolbarConfig.Show(items, item));
            menu.Items.Add(entry);
        }
        add.Flyout = menu;
        PageHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, ResetToolbarButton() } });
    }

    private Button ResetToolbarButton()
    {
        var reset = new Button { Content = Strings.Bar_Reset };
        reset.Click += (_, _) =>
        {
            try { ToolbarConfig.Save(ToolbarPath, ToolbarConfig.Defaults); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Error("saving toolbar.json failed", ex); }
            ShowPage(_currentPage);
        };
        return reset;
    }

    private static void AddIconButton(Grid row, int column, string glyph, string tip, bool enabled, Action click)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 12 }, IsEnabled = enabled, Padding = new Thickness(8, 4, 8, 4) };
        ToolTipService.SetToolTip(button, tip);
        button.Click += (_, _) => click();
        Grid.SetColumn(button, column);
        row.Children.Add(button);
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
                if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
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
            var delete = new Button { Content = Strings.Common_Delete };
            delete.Click += (_, _) => { actions.Delete(action.Name); ShowPage("Actions"); };
            buttons.Children.Add(edit);
            buttons.Children.Add(delete);
            PageHost.Children.Add(Card("/" + action.Name, action.Description, buttons));
        }
        var create = new Button { Content = Strings.Actions_New, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        create.Click += (_, _) => { CliRunner.OpenInEditor(actions.PathFor(actions.Create())); ShowPage("Actions"); };
        var open = new Button { Content = Strings.Actions_OpenFolder };
        open.Click += (_, _) => { Directory.CreateDirectory(actions.Directory); CliRunner.OpenFolder(actions.Directory); };
        PageHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { create, open } });
    }
}
