using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Hotline.Core.Backends;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Settings;

/// <summary>
/// Settings › Chat › Continue in: the shared list of "Continue this chat in" targets (web pages, Open WebUI, apps), and
/// the same editor for a connection's own list. Everything but the Open WebUI API key (Credential Locker) is in the
/// settings files.
/// </summary>
public sealed partial class SettingsWindow
{
    /// <summary>The shared list (settings.json chat.continueIn), on the Chat page.</summary>
    private void BuildContinueInSection()
    {
        PageHost.Children.Add(new TextBlock
        {
            Text = Strings.ContinueSet_Title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(0, 16, 0, 4),
        });
        PageHost.Children.Add(new TextBlock { Text = Strings.ContinueSet_Intro, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8) });
        var host = new StackPanel { Spacing = 4 };
        PageHost.Children.Add(host);
        BuildTargetList(host, () => ContinueTargets.Shared(_settings.Current), list => _settings.Update(s => s.Chat.ContinueIn = list));
    }

    /// <summary>A connection's "Continue in" choice: the shared list (null), its own list, or none ([]).</summary>
    private UIElement ConnectionContinueIn(BackendProfile p, Func<Action<BackendProfile>, Task> save)
    {
        var panel = new StackPanel { Spacing = 4 };
        var choice = new ComboBox { MinWidth = 220 };
        choice.Items.Add(new ComboBoxItem { Content = Strings.ContinueSet_Shared, Tag = "shared" });
        choice.Items.Add(new ComboBoxItem { Content = Strings.ContinueSet_Own, Tag = "own" });
        choice.Items.Add(new ComboBoxItem { Content = Strings.ContinueSet_None, Tag = "none" });
        string Current() => p.ContinueIn is null ? "shared" : p.ContinueIn.Count == 0 ? "none" : "own";
        choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == Current());
        var own = new StackPanel { Spacing = 4, Margin = new Thickness(16, 0, 0, 0) };
        void ShowOwn()
        {
            own.Children.Clear();
            if (p.ContinueIn is { Count: > 0 })
                BuildTargetList(own, () => p.ContinueIn ?? [], list => _ = save(x => x.ContinueIn = list));
        }
        choice.SelectionChanged += async (_, _) =>
        {
            if (choice.SelectedItem is not ComboBoxItem { Tag: string tag } || tag == Current()) return;
            // Own list: starts as a copy of the shared one (or the llama.cpp entry when that is empty).
            List<ContinueTarget> shared = [.. ContinueTargets.Shared(_settings.Current)];
            await save(x => x.ContinueIn = tag switch
            {
                "shared" => null,
                "none" => [],
                _ => shared.Count > 0 ? shared : [ContinueTargets.ReadyMade[0].Target],
            });
            ShowOwn();
        };
        ShowOwn();
        panel.Children.Add(Card(Strings.ContinueSet_Title, Strings.ContinueSet_ConnectionDesc, choice));
        panel.Children.Add(own);
        return panel;
    }

    /// <summary>One card per target (Edit, Remove), then Add (ready-made entries or a blank web page / Open WebUI / app).</summary>
    private void BuildTargetList(StackPanel host, Func<IReadOnlyList<ContinueTarget>> get, Action<List<ContinueTarget>> set)
    {
        void Rebuild()
        {
            host.Children.Clear();
            var targets = get();
            if (targets.Count == 0) host.Children.Add(new TextBlock { Text = Strings.ContinueSet_Empty, Opacity = 0.7, Margin = new Thickness(4) });
            for (var i = 0; i < targets.Count; i++)
            {
                var index = i;
                var t = targets[i];
                var edit = new Button { Content = Strings.Common_Edit };
                edit.Click += async (_, _) =>
                {
                    if (await EditTargetAsync(t) is not { } changed) return;
                    var list = get().ToList();
                    list[index] = changed;
                    set(list);
                    Rebuild();
                };
                var remove = new Button { Content = Strings.Common_Remove };
                remove.Click += (_, _) =>
                {
                    var list = get().ToList();
                    list.RemoveAt(index);
                    set(list);
                    Rebuild();
                };
                var where = t.Kind == ContinueKind.App ? t.Command : t.Url;
                host.Children.Add(Card(t.Name, $"{KindName(t.Kind)} · {where}", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { edit, remove } }));
            }
            var add = new MenuFlyout();
            foreach (var (_, ready) in ContinueTargets.ReadyMade) AddItem(ready.Name, ready);
            add.Items.Add(new MenuFlyoutSeparator());
            AddItem(Strings.ContinueSet_Web, new ContinueTarget(ContinueKind.Web, Strings.ContinueSet_Web, Url: "https://"));
            AddItem(Strings.ContinueSet_OpenWebUi, new ContinueTarget(ContinueKind.OpenWebUi, "Open WebUI", Url: "http://localhost:3000"));
            AddItem(Strings.ContinueSet_App, new ContinueTarget(ContinueKind.App, Strings.ContinueSet_App, Command: ""));
            host.Children.Add(new DropDownButton { Content = Strings.ContinueSet_Add, Flyout = add, Margin = new Thickness(0, 4, 0, 0) });

            void AddItem(string label, ContinueTarget template)
            {
                var item = new MenuFlyoutItem { Text = label };
                item.Click += async (_, _) =>
                {
                    if (await EditTargetAsync(template) is not { } created) return;
                    set([.. get(), created]);
                    Rebuild();
                };
                add.Items.Add(item);
            }
        }
        Rebuild();
    }

    private static string KindName(ContinueKind kind) => kind switch
    {
        ContinueKind.Web => Strings.ContinueSet_Web,
        ContinueKind.OpenWebUi => Strings.ContinueSet_OpenWebUi,
        _ => Strings.ContinueSet_App,
    };

    /// <summary>The edit dialog: the fields of the target's kind. Null = cancelled.</summary>
    private async Task<ContinueTarget?> EditTargetAsync(ContinueTarget t)
    {
        var name = new TextBox { Header = Strings.ContinueSet_Name, Text = t.Name };
        var where = new TextBox
        {
            Header = t.Kind == ContinueKind.App ? Strings.ContinueSet_Command : Strings.ContinueSet_Address,
            Text = (t.Kind == ContinueKind.App ? t.Command : t.Url) ?? "",
            PlaceholderText = t.Kind switch { ContinueKind.Web => "http://127.0.0.1:8080/?q={chat}", ContinueKind.OpenWebUi => "http://localhost:3000", _ => "" },
        };
        var hint = new TextBlock
        {
            Text = t.Kind switch { ContinueKind.Web => Strings.ContinueSet_AddressHint, ContinueKind.App => Strings.ContinueSet_CommandHint, _ => "" },
            Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        };
        var model = new TextBox { Header = Strings.ContinueSet_Model, Text = t.Model ?? "" };
        var key = new PasswordBox { Header = Strings.ContinueSet_ApiKey };
        var copy = new CheckBox { Content = Strings.ContinueSet_CopyChat, IsChecked = t.CopyChat };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var form = new StackPanel { Spacing = 8, MinWidth = 420, Children = { name, where, hint } };
        if (t.Kind == ContinueKind.OpenWebUi)
        {
            if (_secrets.Get(Chat.ContinueIn.ApiKeyName(t.Url ?? "")) is not null) key.PlaceholderText = Strings.ContinueSet_KeySaved;
            var test = new Button { Content = Strings.ContinueSet_Test };
            test.Click += async (_, _) => status.Text = await TestOpenWebUiAsync(where.Text, key.Password);
            form.Children.Add(model);
            form.Children.Add(key);
            form.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { test, status } });
        }
        form.Children.Add(copy);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = KindName(t.Kind), Content = form,
            PrimaryButtonText = Strings.ContinueSet_Save, CloseButtonText = Strings.Common_Cancel, DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(where.Text) || string.IsNullOrWhiteSpace(name.Text)) { status.Text = Strings.ContinueSet_Invalid; e.Cancel = true; }
        };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return null;

        var url = where.Text.Trim();
        if (t.Kind == ContinueKind.OpenWebUi && key.Password.Trim().Length > 0)
            _secrets.Set(Chat.ContinueIn.ApiKeyName(url), key.Password.Trim()); // only in Credential Locker, never in a file
        return t with
        {
            Name = name.Text.Trim(),
            Url = t.Kind == ContinueKind.App ? null : url,
            Command = t.Kind == ContinueKind.App ? url : null,
            Model = t.Kind == ContinueKind.OpenWebUi && model.Text.Trim().Length > 0 ? model.Text.Trim() : null,
            CopyChat = copy.IsChecked == true,
        };
    }

    /// <summary>Open WebUI's Test: lists its models with the key (typed, or the saved one); https or this PC only.</summary>
    private async Task<string> TestOpenWebUiAsync(string address, string typedKey)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var url)) return Strings.Continue_NotAWebAddress;
        if (url.Scheme != Uri.UriSchemeHttps && !url.IsLoopback) return Strings.Continue_InsecureKey;
        var key = typedKey.Trim().Length > 0 ? typedKey.Trim() : _secrets.Get(Chat.ContinueIn.ApiKeyName(address));
        if (string.IsNullOrWhiteSpace(key)) return Strings.Continue_NoKey;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/api/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return L.F(Strings.ContinueSet_TestFailed, (int)response.StatusCode);
            var models = JsonNode.Parse(await response.Content.ReadAsStringAsync())?["data"]?.AsArray().Count ?? 0;
            return L.F(Strings.ContinueSet_TestOk, models);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return L.F(Strings.ContinueSet_TestFailed, ex.Message);
        }
    }
}
