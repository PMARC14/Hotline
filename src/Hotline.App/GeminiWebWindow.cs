using Hotline.Core.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Hotline.App;

/// <summary>
/// PERSONAL, unofficial (branch personal/gemini-web, not for release): gemini.google.com in a WebView2 window, so
/// Gemini is one click away without a browser open. Just the website in a window — Hotline reads nothing from it.
/// The sign-in is kept in its own WebView2 profile; closing only hides the window. Non-Google links open in the
/// default browser.
/// </summary>
internal sealed class GeminiWebWindow(FileLog log)
{
    private static readonly Uri Home = new("https://gemini.google.com/app");
    private static readonly string[] GoogleHosts = ["google.com", "gstatic.com", "googleusercontent.com", "googleapis.com", "youtube.com"];

    private Window? _window;

    public void Show()
    {
        if (_window is null) Create();
        _window!.AppWindow.Show();
        _window.Activate();
    }

    private void Create()
    {
        var web = new WebView2();
        _window = new Window { Title = "Gemini (web)", Content = web };
        _window.AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 1000));
        _window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Hotline.ico"));
        _window.AppWindow.Closing += (sender, args) => { args.Cancel = true; sender.Hide(); }; // keep the session warm
        _ = InitializeAsync(web);
    }

    private async Task InitializeAsync(WebView2 web)
    {
        try
        {
            // Its own profile folder: the Google sign-in survives restarts and stays separate from Edge.
            var folder = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "GeminiWeb");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, folder, new CoreWebView2EnvironmentOptions());
            await web.EnsureCoreWebView2Async(environment);
            var core = web.CoreWebView2;
            core.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && !IsGoogle(uri)) { e.Cancel = true; OpenOutside(uri); }
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
                if (IsGoogle(uri)) core.Navigate(uri.AbsoluteUri); else OpenOutside(uri);
            };
            core.Navigate(Home.AbsoluteUri);
        }
        catch (Exception ex)
        {
            log.Error("Gemini web: WebView2 failed to start", ex);
            web.Visibility = Visibility.Collapsed;
            _window!.Content = new TextBlock
            {
                Text = "Gemini (web) needs the Microsoft Edge WebView2 Runtime. It's built into Windows 11; see the log for details.",
                Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap,
            };
        }
    }

    private static bool IsGoogle(Uri uri) =>
        uri.Scheme is "https" or "about" or "data" or "blob" &&
        (uri.Scheme != "https" || GoogleHosts.Any(h => uri.Host == h || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)));

    private static void OpenOutside(Uri uri)
    {
        if (uri.Scheme is "https" or "http") _ = Windows.System.Launcher.LaunchUriAsync(uri);
    }
}
