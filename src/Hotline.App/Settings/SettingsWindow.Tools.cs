using Hotline.Core.State;
using Hotline.Core.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Settings;

public sealed partial class SettingsWindow
{
    private void BuildToolsGrantsAndSecrets()
    {
        var dataDir = !string.IsNullOrEmpty(_mcpPath) ? Path.GetDirectoryName(_mcpPath) : Path.GetDirectoryName(_settingsFile);
        if (string.IsNullOrEmpty(dataDir)) return;
        var state = new StateStore(Path.Combine(dataDir, StateStore.FileName));

        BuildGrantsSection(state);
        if (!string.IsNullOrEmpty(_mcpPath))
            BuildSecretsSection(dataDir, state);
    }

    private void BuildGrantsSection(StateStore state)
    {
        PageHost.Children.Add(new TextBlock
        {
            Text = Strings.Tools_GrantsTitle,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 4),
        });
        var status = new InfoBar { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Error };
        var list = new StackPanel { Spacing = 6 };

        void Fill()
        {
            list.Children.Clear();
            var grants = state.GetGrants()
                .OrderBy(g => g.Server, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Tool, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (grants.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = Strings.Tools_GrantsEmpty, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
                return;
            }
            foreach (var grant in grants)
            {
                var revoke = new Button { Content = Strings.Tools_Revoke };
                revoke.Click += (_, _) =>
                {
                    if (!state.RevokeGrant(grant.Server, grant.Tool, grant.Launch))
                    {
                        status.Message = Strings.Tools_RevokeFailed;
                        status.IsOpen = true;
                        return;
                    }
                    status.IsOpen = false;
                    Fill();
                };
                var shortLaunch = grant.Launch.Length > 8 ? grant.Launch[..8] : grant.Launch;
                list.Children.Add(Card($"{grant.Server}/{grant.Tool}", L.F(Strings.Tools_GrantDesc, grant.Server, grant.Tool, shortLaunch), revoke));
            }
        }

        PageHost.Children.Add(status);
        PageHost.Children.Add(list);
        Fill();
    }

    private void BuildSecretsSection(string dataDir, StateStore state)
    {
        PageHost.Children.Add(new TextBlock
        {
            Text = Strings.Tools_SecretsTitle,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 4),
        });
        var feedback = new InfoBar { IsOpen = false, IsClosable = true };
        var list = new StackPanel { Spacing = 6 };

        void Fill()
        {
            list.Children.Clear();
            var config = McpConfig.Load(_mcpPath);
            if (config.Error is not null) return;
            var entries = McpSecrets.FindPlaintextEnv(config);
            if (entries.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = Strings.Tools_SecretsEmpty, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
                return;
            }
            foreach (var entry in entries)
            {
                var move = new Button { Content = Strings.Tools_MoveToLocker };
                move.Click += async (_, _) =>
                {
                    var name = await AskSecretNameAsync(entry);
                    if (name is null) return;
                    var result = McpSecrets.MoveToLocker(dataDir, entry.Server, entry.EnvKey, name, _secrets, state);
                    if (!result.Succeeded)
                    {
                        feedback.Severity = result.Status == McpMoveStatus.ChangedNothingMoved ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
                        feedback.Message = result.Error ?? Strings.Tools_MoveToLockerFailed;
                        feedback.IsOpen = true;
                        Fill();
                        return;
                    }
                    feedback.Severity = InfoBarSeverity.Success;
                    feedback.Message = L.F(Strings.Tools_MovedToLocker, entry.Server, entry.EnvKey);
                    feedback.IsOpen = true;
                    Fill();
                };
                list.Children.Add(Card($"{entry.Server} › {entry.EnvKey}", L.F(Strings.Tools_EnvPlaintextDesc, entry.Server, entry.EnvKey), move));
            }
        }

        PageHost.Children.Add(feedback);
        PageHost.Children.Add(list);
        Fill();
    }

    private async Task<string?> AskSecretNameAsync(McpPlaintextEnvEntry entry)
    {
        var nameBox = new TextBox { Header = Strings.Tools_SecretName, Text = entry.SuggestedSecretName };
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85, FontSize = 12 };
        var dialog = new ContentDialog
        {
            Title = Strings.Tools_MoveToLockerTitle,
            PrimaryButtonText = Strings.Tools_MoveToLockerConfirm,
            CloseButtonText = Strings.Common_Cancel,
            DefaultButton = ContentDialogButton.Close,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = L.F(Strings.Tools_MoveToLockerBody, entry.Server, entry.EnvKey), TextWrapping = TextWrapping.Wrap },
                    nameBox,
                    hint,
                },
            },
        };

        void Validate()
        {
            var candidate = nameBox.Text.Trim();
            if (!McpSecrets.IsValidSecretName(candidate))
            {
                dialog.IsPrimaryButtonEnabled = false;
                hint.Text = Strings.Tools_SecretNameInvalid;
                hint.Visibility = Visibility.Visible;
                return;
            }
            dialog.IsPrimaryButtonEnabled = true;
            var exists = false;
            try { exists = _secrets.Get(McpSecrets.LockerKey(candidate)) is not null; } catch (Exception) { /* treat unreadable check cleanly */ }
            hint.Text = exists ? Strings.Tools_SecretOverwriteWarn : "";
            hint.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
        }

        nameBox.TextChanged += (_, _) => Validate();
        Validate();
        return await ShowDialogAsync(dialog) == ContentDialogResult.Primary ? nameBox.Text.Trim() : null;
    }
}
