using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Theming;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Hotline.App.Chat;

/// <summary>Bridges the WebView2 chat view and the ChatController. All members run on the UI thread.</summary>
internal sealed partial class ChatHost(
    PopupWindow popup, ChatController chat, AttachmentTray tray, HotlineSettings settings, SettingsStore store, FileLog log,
    Func<IReadOnlyList<BackendProfile>> profiles)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string Origin = "https://hotline.app/";
    private bool _ready;

    public async Task InitializeAsync(string dataDir)
    {
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(dataDir, "WebView2"), new CoreWebView2EnvironmentOptions());
        await popup.Web.EnsureCoreWebView2Async(env);
        var core = popup.Web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("hotline.app", Path.Combine(AppContext.BaseDirectory, "Web"),
            CoreWebView2HostResourceAccessKind.Allow);
        var devtools = App.IsDebugBuild || settings.Diagnostics.VerboseLogging;
        core.Settings.AreDevToolsEnabled = devtools;
        core.Settings.AreDefaultContextMenusEnabled = devtools;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.WebMessageReceived += (_, e) => Guard("web message", () => OnWebMessage(e.WebMessageAsJson));
        core.NewWindowRequested += (_, e) => { e.Handled = true; OpenLink(e.Uri); };
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            OpenLink(e.Uri);
        };
        popup.Web.Source = new Uri(Origin + "index.html");

        chat.Event += e => Guard("chat event", () => OnChatEvent(e));
        popup.Shown += () => Post(new { type = "focus" });
        popup.NewChatRequested += NewChat;
        popup.CaptureRequested += window => Run("capture", () => CaptureAsync(window));
    }

    public void NewChat()
    {
        chat.NewChat();
        tray.TakeAll();
        Post(new { type = "attachmentsCleared" });
    }

    private void OnWebMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var m = doc.RootElement;
        string Str(string name) => m.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
        var type = Str("type");
        log.Debug($"web -> host: {type}");
        switch (type)
        {
            case "ready":
                _ready = true;
                log.Info("chat view ready");
                PostTheme();
                PostBackends();
                break;
            case "send": Run("send", () => SendAsync(Str("text"))); break;
            case "retry": Run("retry", chat.RetryAsync); break;
            case "cancel": chat.Cancel(); break;
            case "newChat": NewChat(); break;
            case "pickFiles": Run("pick files", PickFilesAsync); break;
            case "captureWindow": Run("capture window", () => CaptureAsync(window: true)); break;
            case "captureScreen": Run("capture screen", () => CaptureAsync(window: false)); break;
            case "pasteImage" or "dropFile":
                Run("add file", () => AddBytesAsync(Str("name"), Str("mime"), Convert.FromBase64String(Str("base64"))));
                break;
            case "removeAttachment":
                if (tray.Remove(Str("id"))) Post(new { type = "attachmentRemoved", id = Str("id") });
                break;
            case "selectBackend": SelectBackend(Str("id")); break;
            case "height": popup.SetContentHeight(m.GetProperty("value").GetDouble()); break;
            case "escape": popup.HidePopup(); break;
            case "openLink": OpenLink(Str("url")); break;
        }
    }

    private async Task SendAsync(string text)
    {
        if (!chat.CanAccept(tray.Items, out var reason))
        {
            Toast(reason!);
            return;
        }
        var attachments = tray.TakeAll();
        Post(new { type = "attachmentsCleared" });
        log.Info($"chat send via {chat.BackendId}: {text.Length} chars, {attachments.Count} attachment(s)");
        await chat.SendAsync(text, attachments);
    }

    private void OnChatEvent(ChatEvent e)
    {
        switch (e)
        {
            case UserMessageAdded u:
                Post(new { type = "user", id = u.Message.Id, text = u.Message.Text,
                    attachments = u.Message.Attachments.Select(a => new { name = a.Name, kind = a.Kind.ToString() }) });
                break;
            case AssistantStarted s: Post(new { type = "assistantStart", id = s.Id, backend = s.BackendName }); break;
            case AssistantDelta d: Post(new { type = "delta", id = d.Id, text = d.Text, replace = d.Replace }); break;
            case AssistantCompleted c: log.Info("chat answer completed"); Post(new { type = "done", id = c.Id }); break;
            case AssistantCancelled c: Post(new { type = "cancelled", id = c.Id }); break;
            case AssistantFailed f:
                log.Error($"chat answer failed: {f.Kind}: {f.Message}");
                Post(new { type = "error", id = f.Id, code = f.Kind.ToString(), message = f.Message });
                break;
            case ConversationReset: Post(new { type = "reset" }); break;
        }
    }

    private void SelectBackend(string id)
    {
        if (profiles().All(p => p.Id != id)) return;
        chat.BackendId = id;
        settings.Chat.DefaultBackend = id;
        try { store.Save(settings); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Error("saving backend choice failed", ex); }
        PostBackends();
    }

    private void PostTheme()
    {
        var dark = settings.Window.Theme switch
        {
            ThemeChoice.Dark => true,
            ThemeChoice.Light => false,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
        };
        Post(new { type = "theme", css = ThemeTokens.For(dark, settings.Window).ToCss(), scrollbar = settings.Window.Scrollbar.ToString().ToLowerInvariant() });
    }

    private void PostBackends() => Post(new
    {
        type = "backends",
        items = profiles().Select(p => new { id = p.Id, name = p.Name, available = BackendFactory.IsAvailable(p.Type) }),
        selected = chat.BackendId,
    });

    private void Toast(string message) => Post(new { type = "toast", message });

    private void Post(object message)
    {
        if (!_ready || popup.Web.CoreWebView2 is null) return;
        popup.Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
    }

    private void OpenLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
    }

    private void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { log.Error($"{what} failed", ex); }
    }

    private void Run(string what, Func<Task> work) => _ = RunAsync(what, work);

    private async Task RunAsync(string what, Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex)
        {
            log.Error($"{what} failed", ex);
            Toast($"Couldn't {what}: {ex.Message}");
        }
    }

    // Filled in by Task 9 (attachments & capture). Until then they report "coming soon".
    private partial Task PickFilesAsync();
    private partial Task CaptureAsync(bool window);
    private partial Task AddBytesAsync(string name, string? mime, byte[] data);
}
