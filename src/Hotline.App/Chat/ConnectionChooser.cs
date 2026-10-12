using Hotline.Core.Backends;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Backends.ClaudeCode;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Chat;

/// <summary>
/// First run (chat.defaultBackend empty): instead of starting a CLI nobody chose, the panel opens on a card listing the
/// connections that can work on this PC (installed Claude Code / Antigravity) plus API keys, and a link to the Copilot
/// key setting. Nothing is sent and no CLI runs until one is picked. The card comes back on each open (and on a send)
/// until then; choosing in the toolbar or in Settings works too.
/// </summary>
internal sealed class ConnectionChooser(PopupWindow popup, NoticeArea notices, SettingsService settings, Action<string> select,
    Action openConnectionSettings, FileLog log)
{
    private const string Tag = "choose-connection";

    public static bool Chosen(HotlineSettings s) => !string.IsNullOrEmpty(s.Chat.DefaultBackend);

    public void Initialize()
    {
        notices.Sticky.Add(Tag);
        popup.Shown += () => { if (!Chosen(settings.Current)) Show(); };
        if (popup.IsShown && !Chosen(settings.Current)) Show(); // already open when this was set up
        settings.Changed += () => popup.DispatcherQueue.TryEnqueue(() => { if (Chosen(settings.Current)) notices.Remove(Tag); });
    }

    /// <summary>Shows (or refreshes) the card. The installed-CLI lookup (file checks over PATH) runs off the UI thread.</summary>
    public void Show() => _ = ShowAsync();

    private async Task ShowAsync()
    {
        List<BackendType> installed;
        try { installed = await Task.Run(Installed); }
        catch (Exception ex) { log.Error("looking for installed CLIs failed", ex); installed = []; }
        if (Chosen(settings.Current)) return; // picked meanwhile
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var type in installed)
        {
            var button = new Button { Content = L.F(Strings.Choose_Use, ConnectionTypes.Of(type).DisplayName) };
            if (buttons.Children.Count == 0 && Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) && style is Style s)
                button.Style = s;
            button.Click += (_, _) => Pick(type);
            buttons.Children.Add(button);
        }
        var apiKey = new Button { Content = Strings.Choose_ApiKey };
        apiKey.Click += (_, _) => openConnectionSettings();
        buttons.Children.Add(apiKey);
        var copilotKey = new HyperlinkButton { Content = Strings.Choose_CopilotKey, Padding = new Thickness(0, 2, 0, 2) };
        copilotKey.Click += (_, _) => _ = CopilotKeySettings.OpenAsync(log);
        notices.ShowTagged(Tag, Strings.Choose_Title, installed.Count > 0 ? Strings.Choose_Body : Strings.Choose_NoneFound,
            InfoBarSeverity.Informational, new StackPanel
            {
                Spacing = 4, Margin = new Thickness(0, 0, 0, 12),
                Children =
                {
                    new ScrollViewer
                    {
                        Content = buttons, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollMode = ScrollMode.Disabled,
                    },
                    copilotKey,
                },
            });
    }

    private static List<BackendType> Installed()
    {
        var found = new List<BackendType>();
        var path = Environment.GetEnvironmentVariable("PATH");
        try
        {
            if (ClaudeLocator.Find(null, File.Exists, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path) is not null)
                found.Add(BackendType.ClaudeCode);
            if (AgyLocator.Find(null, File.Exists, Environment.GetEnvironmentVariable("LOCALAPPDATA"), path) is not null)
                found.Add(BackendType.Antigravity);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return found;
    }

    private void Pick(BackendType type)
    {
        var id = settings.Current.Chat.Backends.FirstOrDefault(b => b.Type == type)?.Id;
        if (id is null) settings.Update(s => id = ConnectionEditor.Add(s.Chat, type).Id);
        log.Info($"first connection chosen: {type}");
        notices.Remove(Tag);
        select(id!);
    }
}

/// <summary>Windows Settings › Personalization › Text input › Customize Copilot key on keyboard (where Hotline is picked).</summary>
internal static class CopilotKeySettings
{
    public static async Task OpenAsync(FileLog log)
    {
        try
        {
            // Windows builds without this page open Settings on its home page (LaunchUriAsync still reports success).
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:personalization-textinput-copilot-hardwarekey"));
        }
        catch (Exception ex) { log.Error("could not open the Copilot key setting", ex); }
    }
}
