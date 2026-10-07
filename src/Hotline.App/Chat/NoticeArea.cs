using System.Text.Json;
using Hotline.Core.Diagnostics;
using Hotline.Core.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Chat;

/// <summary>
/// The info bars above the message box: passing notices (at most three, gone after 10 s), bars that stay until removed
/// (by tag, e.g. "Listening…"), and tool-approval questions, which are never pushed out.
/// </summary>
internal sealed class NoticeArea(PopupWindow popup, FileLog log)
{
    private const string ApprovalTag = "approval";

    private StackPanel Panel => popup.NoticesPanel;

    public void Show(string message, InfoBarSeverity severity)
    {
        var bar = new InfoBar { IsOpen = true, IsClosable = true, Severity = severity, Message = message };
        bar.Closed += (_, _) => Panel.Children.Remove(bar);
        Panel.Children.Add(bar);
        // Keep at most 3 passing notices; tagged bars (approvals, listening) are never pushed out.
        while (Panel.Children.OfType<InfoBar>().Count(b => b.Tag is null) > 3
               && Panel.Children.OfType<InfoBar>().FirstOrDefault(b => b.Tag is null) is { } oldest)
            Panel.Children.Remove(oldest);
        var timer = popup.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(10);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Panel.Children.Remove(bar);
        timer.Start();
    }

    /// <summary>A bar that stays until <see cref="Remove"/> with the same tag (replaces an earlier one with that tag).</summary>
    public void ShowTagged(string tag, string? title, string message, InfoBarSeverity severity, bool closable = false, (string Text, Action Click)? action = null)
    {
        Remove(tag);
        var bar = new InfoBar { Tag = tag, IsOpen = true, IsClosable = closable, Severity = severity, Title = title ?? "", Message = message };
        if (action is { } a)
        {
            var button = new Button { Content = a.Text };
            button.Click += (_, _) => a.Click();
            bar.ActionButton = button;
        }
        bar.Closed += (_, _) => Panel.Children.Remove(bar);
        Panel.Children.Add(bar);
    }

    public void Remove(string tag)
    {
        foreach (var old in Panel.Children.OfType<InfoBar>().Where(b => tag.Equals(b.Tag)).ToList()) Panel.Children.Remove(old);
    }

    public void Clear() => Panel.Children.Clear();

    /// <summary>
    /// Asks whether a tool may run: a bar with Allow once / Always / Deny. No answer within five minutes (or the answer
    /// being stopped) counts as Deny. Brings the panel up if it was hidden. Callable from any thread.
    /// </summary>
    public Task<ToolDecision> AskToolApprovalAsync(ToolCallRequest request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ToolDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Anything that goes wrong (shutting down, UI failure, cancellation) means Deny — never an endless wait.
        var registration = ct.Register(() => tcs.TrySetResult(ToolDecision.Deny));
        _ = tcs.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        if (!popup.DispatcherQueue.TryEnqueue(() =>
        {
            try { ShowApproval(request, tcs); }
            catch (Exception ex)
            {
                log.Error("showing the tool approval failed", ex);
                tcs.TrySetResult(ToolDecision.Deny);
            }
        }))
            tcs.TrySetResult(ToolDecision.Deny);
        return tcs.Task;
    }

    /// <summary>The approval bar: the full arguments (pretty-printed, scrollable, selectable) — nothing is hidden.</summary>
    private void ShowApproval(ToolCallRequest request, TaskCompletionSource<ToolDecision> tcs)
    {
        var args = request.Arguments.ValueKind == JsonValueKind.Undefined ? "{}"
            : JsonSerializer.Serialize(request.Arguments, new JsonSerializerOptions { WriteIndented = true });
        var description = request.Tool.Description.Trim();
        if (description.Length > 200) description = description[..200] + "…";
        var details = new StackPanel { Spacing = 4 };
        if (description.Length > 0) details.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        details.Children.Add(new ScrollViewer
        {
            MaxHeight = 160, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = args, IsTextSelectionEnabled = true, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas") },
        });
        var bar = new InfoBar
        {
            IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Tag = ApprovalTag,
            Title = $"Allow {request.Tool.Server} › {request.Tool.Tool}?",
        };
        var timer = popup.DispatcherQueue.CreateTimer();
        void Answer(ToolDecision d)
        {
            timer.Stop();
            Panel.Children.Remove(bar);
            if (tcs.TrySetResult(d)) log.Info($"tool {request.Tool.Server}/{request.Tool.Tool}: {d}");
        }
        Button B(string text, ToolDecision d, bool accent = false)
        {
            var b = new Button { Content = text };
            if (accent && Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) && style is Style s) b.Style = s;
            b.Click += (_, _) => Answer(d);
            return b;
        }
        details.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 8),
            Children = { B("Allow once", ToolDecision.AllowOnce, accent: true), B("Always allow", ToolDecision.AllowAlways), B("Deny", ToolDecision.Deny) },
        });
        bar.Content = details;
        Panel.Children.Add(bar);
        if (!popup.IsShown) popup.ShowPopup();
        timer.Interval = TimeSpan.FromMinutes(5);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Answer(ToolDecision.Deny);
        timer.Start();
        // Cancelled or timed out elsewhere: take the bar down.
        _ = tcs.Task.ContinueWith(_ => popup.DispatcherQueue.TryEnqueue(() => { timer.Stop(); Panel.Children.Remove(bar); }), TaskScheduler.Default);
    }
}
