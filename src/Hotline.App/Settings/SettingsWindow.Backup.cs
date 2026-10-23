using Hotline.App.Chat;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Hotline.App.Settings;

/// <summary>
/// Settings › Backup: export the ticked parts of the data folder to one .zip, or import such a file
/// (<see cref="SettingsPackage"/> does the checking, backing up and writing). Files are read and written on a background
/// task; the page only shows the outcome.
/// </summary>
public sealed partial class SettingsWindow
{
    private string? _importPath;

    private void BuildBackupPage()
    {
        PageHost.Children.Add(new TextBlock { Text = Strings.Backup_Intro, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8) });
        var status = new StackPanel { Spacing = 8 }; // the result InfoBars go here, under both cards

        // ---- export ----
        var boxes = new Dictionary<SettingsCategory, CheckBox>();
        var export = new Button { Content = Strings.Backup_ExportButton };
        AutomationProperties.SetAutomationId(export, "backup-export");
        void Refresh() => export.IsEnabled = boxes.Values.Any(b => b.IsChecked == true);
        var list = new StackPanel { Spacing = 4 };
        foreach (var category in SettingsPackage.All)
        {
            var box = new CheckBox { Content = SettingsPackage.Name(category), IsChecked = true };
            AutomationProperties.SetAutomationId(box, "backup-export-" + category.ToString().ToLowerInvariant());
            box.Checked += (_, _) => Refresh();
            box.Unchecked += (_, _) => Refresh();
            boxes[category] = box;
            list.Children.Add(box);
            var note = category switch { SettingsCategory.Mcp => Strings.Backup_McpWarn, SettingsCategory.Connections => Strings.Backup_ConnNote, _ => null };
            if (note is not null)
                list.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(28, 0, 0, 4) });
        }
        Button SetAll(string text, bool on)
        {
            var b = new Button { Content = text };
            b.Click += (_, _) => { foreach (var box in boxes.Values) box.IsChecked = on; };
            return b;
        }
        export.Click += async (_, _) =>
        {
            var chosen = boxes.Where(b => b.Value.IsChecked == true).Select(b => b.Key).ToList();
            try
            {
                var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = Path.GetFileNameWithoutExtension(SettingsPackage.FileName(DateTimeOffset.Now)) };
                picker.FileTypeChoices.Add(Strings.Backup_ExportPickerType, [".zip"]);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                if (await picker.PickSaveFileAsync() is not { } file) return;
                export.IsEnabled = false;
                var data = DataDirectory;
                var result = await Task.Run(() =>
                {
                    using var stream = new FileStream(file.Path, FileMode.Create, FileAccess.Write, FileShare.None);
                    return SettingsPackage.Export(data, chosen, stream);
                });
                _log.Info($"settings exported: {result.FileCount} files, {result.Skipped.Count} left out");
                ShowStatus(status, InfoBarSeverity.Success, result.Skipped.Count == 0
                    ? L.F(Strings.Backup_Exported, file.Path, result.FileCount)
                    : L.F(Strings.Backup_ExportedSkipped, file.Path, result.FileCount, result.Skipped.Count));
            }
            catch (Exception ex)
            {
                // A handler of its own: nothing may escape an async event handler.
                _log.Error("settings export failed", ex);
                ShowStatus(status, InfoBarSeverity.Error, L.F(Strings.Backup_ExportFailed, ex.Message));
            }
            finally { Refresh(); }
        };
        PageHost.Children.Add(new Border
        {
            Style = (Style)Root.Resources["CardStyle"],
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = Strings.Backup_Export_Title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] },
                    new TextBlock { Text = Strings.Backup_Export_Desc, Opacity = 0.7 },
                    list,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { SetAll(Strings.Backup_SelectAll, true), SetAll(Strings.Backup_SelectNone, false), export } },
                },
            },
        });

        // ---- import ----
        var contents = new StackPanel { Spacing = 4 };
        var choose = new Button { Content = Strings.Backup_Choose };
        AutomationProperties.SetAutomationId(choose, "backup-import-choose");
        var run = new Button { Content = Strings.Backup_ImportButton, IsEnabled = false, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(run, "backup-import-run");
        var importBoxes = new Dictionary<SettingsCategory, CheckBox>();
        void RefreshImport() => run.IsEnabled = _importPath is not null && importBoxes.Values.Any(b => b.IsChecked == true);

        choose.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                picker.FileTypeFilter.Add(".zip");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                if (await picker.PickSingleFileAsync() is not { } file) return;
                var info = await Task.Run(() =>
                {
                    using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return SettingsPackage.Inspect(stream);
                });
                contents.Children.Clear();
                importBoxes.Clear();
                _importPath = null;
                run.Visibility = Visibility.Collapsed;
                if (!info.IsValid)
                {
                    ShowStatus(status, InfoBarSeverity.Error, L.F(Strings.Backup_PackageInvalid, string.Join(" ", info.Problems.Take(3))));
                    return;
                }
                if (info.Counts.Count == 0)
                {
                    ShowStatus(status, InfoBarSeverity.Informational, Strings.Backup_PackageEmpty);
                    return;
                }
                _importPath = file.Path;
                contents.Children.Add(new TextBlock
                {
                    Text = L.F(Strings.Backup_PackageInfo, info.Created?.ToLocalTime().ToString("g") ?? "?", info.AppVersion ?? "?"),
                    Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
                });
                foreach (var category in SettingsPackage.All.Where(info.Counts.ContainsKey))
                {
                    var box = new CheckBox { Content = L.F(Strings.Backup_PackageCount, SettingsPackage.Name(category), info.Counts[category]), IsChecked = true };
                    AutomationProperties.SetAutomationId(box, "backup-import-" + category.ToString().ToLowerInvariant());
                    box.Checked += (_, _) => RefreshImport();
                    box.Unchecked += (_, _) => RefreshImport();
                    importBoxes[category] = box;
                    contents.Children.Add(box);
                }
                run.Visibility = Visibility.Visible;
                RefreshImport();
            }
            catch (Exception ex)
            {
                _log.Error("reading a settings package failed", ex);
                ShowStatus(status, InfoBarSeverity.Error, L.F(Strings.Backup_PackageInvalid, ex.Message));
            }
        };
        run.Click += async (_, _) =>
        {
            if (_importPath is not { } path) return;
            var chosen = importBoxes.Where(b => b.Value.IsChecked == true).Select(b => b.Key).ToList();
            run.IsEnabled = false;
            try
            {
                var data = DataDirectory;
                var (result, needKeys) = await Task.Run(() =>
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var r = SettingsPackage.Import(data, stream, chosen, Path.Combine(data, SettingsReset.BackupsFolder));
                    return (r, r.Error is null ? SettingsPackage.ConnectionsNeedingKey(data, r, _secrets) : []);
                });
                _log.Info($"settings imported: {result.Written.Count} files, {result.Skipped.Count} skipped" + (result.Error is null ? "" : ", refused"));
                foreach (var s in result.Skipped.Take(20)) _log.Info($"import skipped {SettingsPackage.LogName(s.Path)}: {s.Reason}");
                if (result.Error is { } error)
                {
                    ShowStatus(status, InfoBarSeverity.Error, L.F(Strings.Backup_ImportFailed, error));
                    return;
                }
                var lines = new List<string> { L.F(Strings.Backup_ImportDone, result.Written.Count, result.Skipped.Count) };
                lines.AddRange(result.Skipped.Take(5).Select(s => L.F(Strings.Backup_ImportSkipped, SettingsPackage.LogName(s.Path) + " - " + s.Reason)));
                if (result.BackupFolder is { } backup) lines.Add(L.F(Strings.Backup_ImportBackupAt, backup));
                if (needKeys.Count > 0) lines.Add(L.F(Strings.Backup_NeedKeys, string.Join(", ", needKeys)));
                lines.Add(Strings.Backup_LiveNote);
                var bar = ShowStatus(status, needKeys.Count > 0 || result.Skipped.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, string.Join("\n", lines));
                if (result.BackupFolder is { } folder)
                {
                    var open = new Button { Content = Strings.Backup_OpenBackup };
                    open.Click += (_, _) => CliRunner.OpenFolder(folder);
                    bar.ActionButton = open;
                }
            }
            catch (Exception ex)
            {
                _log.Error("settings import failed", ex);
                ShowStatus(status, InfoBarSeverity.Error, L.F(Strings.Backup_ImportFailed, ex.Message));
            }
            finally { RefreshImport(); }
        };
        PageHost.Children.Add(new Border
        {
            Style = (Style)Root.Resources["CardStyle"],
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = Strings.Backup_Import_Title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] },
                    new TextBlock { Text = Strings.Backup_Import_Desc, Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = Strings.Backup_ConnNote, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                    choose, contents, run,
                },
            },
        });
        PageHost.Children.Add(status);
        Refresh();
    }

    /// <summary>Replaces the status area's content with one InfoBar.</summary>
    private static InfoBar ShowStatus(StackPanel host, InfoBarSeverity severity, string message)
    {
        var bar = new InfoBar { IsOpen = true, IsClosable = true, Severity = severity, Message = message };
        host.Children.Clear();
        host.Children.Add(bar);
        return bar;
    }
}
