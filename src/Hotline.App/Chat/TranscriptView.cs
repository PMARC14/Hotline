using System.Diagnostics;
using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Hotline.App.Chat;

/// <summary>
/// The conversation as ONE selectable RichTextBlock, so a drag selects across messages, paragraphs, lists and code.
/// Every message owns a contiguous run of paragraphs; only the last message (the streaming answer) ever changes, so
/// updates remove and re-append paragraphs at the end. Also keeps the view scrolled to the newest text while the
/// user is at the bottom. No TextHighlighters: drawing them crashes WinUI (access violation in Microsoft.UI.Xaml.dll).
/// </summary>
internal sealed class TranscriptView(PopupWindow popup, PanelTheme theme, AttachmentVisuals visuals, NoticeArea notices, Action onRetry)
{
    private abstract class Entry;

    private sealed class UserEntry(ChatMessage message) : Entry
    {
        public ChatMessage Message { get; } = message;
    }

    private sealed class AssistantView : Entry
    {
        public required string BackendName { get; init; }
        /// <summary>The provider's report page, offered in the answer's ⋯ menu (null: no menu).</summary>
        public ReportTarget? Report { get; init; }
        public IReadOnlyList<MdBlock> Blocks { get; set; } = [];
        /// <summary>Per rendered markdown block: how many paragraphs it added.</summary>
        public List<int> Rendered { get; } = [];
        public Paragraph? CaretParagraph { get; set; }
        public string Text { get; set; } = "";
        public string? Error { get; set; }
        public bool Streaming { get; set; } = true;
        public bool Dirty { get; set; }
        /// <summary>A failed attempt that was retried: no longer shown.</summary>
        public bool Superseded { get; set; }
    }

    private readonly Dictionary<string, AssistantView> _assistants = [];
    private readonly List<Entry> _entries = [];
    private RichTextBlock _block = null!;
    private RenderTimer? _timer;
    private bool _stickToBottom = true;
    private bool _autoScrolling;

    public bool IsEmpty => _block is null || _block.Blocks.Count == 0;
    public int ParagraphCount => _block.Blocks.Count;
    public int EmbeddedControlCount => _block.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineUIContainer>().Count();

    public void Initialize()
    {
        _block = new RichTextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontSize = theme.Tokens.FontSizePx, FontFamily = theme.Style.Font };
        popup.MessagesPanel.Children.Clear();
        popup.MessagesPanel.Children.Add(_block);
        _timer = new RenderTimer(popup.DispatcherQueue, TimeSpan.FromMilliseconds(50), RenderDirty);
        popup.MessagesPanel.SizeChanged += (_, _) => { if (_stickToBottom) ScrollToBottomNow(); };
        popup.MessagesScroll.ViewChanged += OnViewChanged;
        popup.JumpToLatestButton.Click += (_, _) => { _stickToBottom = true; UpdateJumpButton(); ScrollToBottomNow(); };
    }

    private double ContentWidth => Math.Max(200, popup.MessagesPanel.ActualWidth - popup.MessagesPanel.Padding.Left - popup.MessagesPanel.Padding.Right - 4);

    public void Clear()
    {
        _entries.Clear();
        _assistants.Clear();
        _block.Blocks.Clear();
    }

    /// <summary>Re-renders everything (font/theme changes).</summary>
    public void Rebuild()
    {
        _block.FontSize = theme.Tokens.FontSizePx;
        _block.FontFamily = theme.Style.Font;
        _block.Blocks.Clear();
        foreach (var entry in _entries)
        {
            if (entry is UserEntry u) AppendUser(u.Message);
            else if (entry is AssistantView { Superseded: false } a)
            {
                a.Blocks = [];
                a.Rendered.Clear();
                a.CaretParagraph = null;
                AppendHeader(a);
                Render(a);
                if (!a.Streaming) AppendFooter(a, withError: entry == _entries[^1]);
            }
        }
    }

    // ---- messages ----------------------------------------------------------------------------

    public void AddUser(ChatMessage message)
    {
        _entries.Add(new UserEntry(message));
        AppendUser(message);
        ScrollToEnd(force: true);
    }

    private void AppendUser(ChatMessage message)
    {
        _block.Blocks.Add(Spacer());
        var label = new Paragraph { TextAlignment = TextAlignment.Right, FontSize = 11, Foreground = theme.Style.Accent, Margin = new Thickness(80, 0, 0, 2) };
        label.Inlines.Add(new Run { Text = "You" });
        _block.Blocks.Add(label);
        if (message.Text.Length > 0)
        {
            var p = new Paragraph { TextAlignment = TextAlignment.Right, Margin = new Thickness(80, 0, 0, 4) };
            AddLines(p, message.Text);
            _block.Blocks.Add(p);
        }
        if (message.Attachments.Count > 0)
        {
            var p = new Paragraph { TextAlignment = TextAlignment.Right, Margin = new Thickness(80, 0, 0, 4) };
            p.Inlines.Add(new InlineUIContainer { Child = visuals.Strip(message.Attachments, thumbSize: 72) });
            _block.Blocks.Add(p);
        }
    }

    public void StartAnswer(string id, string backendName, ReportTarget? report = null)
    {
        var view = new AssistantView { BackendName = backendName, Report = report, Dirty = true };
        _assistants[id] = view;
        _entries.Add(view);
        AppendHeader(view);
        _timer?.Start();
    }

    public bool HasAnswer(string id) => _assistants.ContainsKey(id);

    public void AppendToAnswer(string id, string text, bool replace)
    {
        if (!_assistants.TryGetValue(id, out var view)) return;
        view.Text = replace ? text : view.Text + text;
        view.Dirty = true;
        _timer?.Start();
    }

    /// <summary>A saved answer from history (complete, no streaming).</summary>
    public void RestoreAnswer(ChatMessage message, string backendName, ReportTarget? report = null)
    {
        var view = new AssistantView { BackendName = backendName, Report = report, Text = message.Text, Streaming = false };
        _assistants[message.Id] = view;
        _entries.Add(view);
        AppendHeader(view);
        Render(view);
        AppendFooter(view, withError: false);
        ScrollToEnd(force: true);
    }

    public void FinishAnswer(string id)
    {
        if (!_assistants.TryGetValue(id, out var view)) return;
        view.Dirty = true;
        RenderDirty();
        view.Streaming = false;
        RemoveCaret(view);
        AppendFooter(view, withError: true);
    }

    public void ShowError(string id, string message)
    {
        if (!_assistants.TryGetValue(id, out var view)) return;
        view.Error = message;
        if (_entries.Count > 0 && _entries[^1] == view) AppendError(view);
        ScrollToEnd(force: false);
    }

    private void AppendHeader(AssistantView view)
    {
        _block.Blocks.Add(Spacer());
        if (view.BackendName.Length == 0) return;
        var label = new Paragraph { FontSize = 11, Foreground = theme.Style.Muted, Margin = new Thickness(0, 0, 0, 2) };
        label.Inlines.Add(new Run { Text = view.BackendName });
        _block.Blocks.Add(label);
    }

    private void AppendFooter(AssistantView view, bool withError)
    {
        if (view.Text.Length > 0)
        {
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
            p.Inlines.Add(new InlineUIContainer { Child = CopyButton(() => view.Text, "Copy response") });
            if (view.Report is { } report) p.Inlines.Add(new InlineUIContainer { Child = MoreButton(report) });
            _block.Blocks.Add(p);
        }
        if (withError && view.Error is not null) AppendError(view);
    }

    private void AppendError(AssistantView view)
    {
        var bar = new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Error, Message = view.Error, Width = ContentWidth };
        var retry = new Button { Content = "Retry" };
        retry.Click += (_, _) =>
        {
            view.Error = null;
            view.Superseded = true; // the retried answer replaces this attempt
            // Rebuild after this click finishes: the clicked button lives inside the transcript being rebuilt.
            popup.DispatcherQueue.TryEnqueue(() => { Rebuild(); onRetry(); });
        };
        bar.ActionButton = retry;
        var p = new Paragraph();
        p.Inlines.Add(new InlineUIContainer { Child = bar });
        _block.Blocks.Add(p);
    }

    /// <summary>
    /// Re-renders changed answers. Only blocks from the first changed one onward are rebuilt (earlier paragraphs stay
    /// put, keeping selection), and the interval adapts to how long rendering takes.
    /// </summary>
    public void RenderDirty()
    {
        var watch = Stopwatch.StartNew();
        var any = false;
        foreach (var view in _assistants.Values.Where(v => v.Dirty))
        {
            view.Dirty = false;
            any = true;
            if (_entries.Count > 0 && _entries[^1] == view && view.Streaming) Render(view);
        }
        if (!any) { _timer?.Stop(); return; }
        _timer?.SetInterval(RenderThrottle.NextInterval(watch.Elapsed));
        ScrollToEnd(force: false);
    }

    /// <summary>Brings the (last) answer's paragraphs up to date with its text.</summary>
    private void Render(AssistantView view)
    {
        var blocks = MarkdownModel.Parse(view.Text);
        var from = MarkdownModel.FirstChangedIndex(view.Blocks, blocks);
        RemoveCaret(view);
        while (view.Rendered.Count > from)
        {
            var paragraphs = view.Rendered[^1];
            view.Rendered.RemoveAt(view.Rendered.Count - 1);
            for (var k = 0; k < paragraphs; k++) _block.Blocks.RemoveAt(_block.Blocks.Count - 1);
        }
        for (var b = from; b < blocks.Count; b++) view.Rendered.Add(MarkdownRenderer.AppendBlock(_block, blocks[b], theme.Style, ContentWidth));
        view.Blocks = blocks;
        if (view.Streaming)
        {
            view.CaretParagraph = new Paragraph();
            view.CaretParagraph.Inlines.Add(new Run { Text = "▍", Foreground = theme.Style.Accent });
            _block.Blocks.Add(view.CaretParagraph);
        }
    }

    private void RemoveCaret(AssistantView view)
    {
        if (view.CaretParagraph is null) return;
        _block.Blocks.Remove(view.CaretParagraph);
        view.CaretParagraph = null;
    }

    // ---- scrolling -----------------------------------------------------------------------------

    /// <summary>
    /// Follow the conversation while the user is at the bottom; stop following once they scroll up to read (resumes
    /// when they scroll back down or send a message). Scrolling happens after layout, so the target is the new bottom.
    /// </summary>
    public void ScrollToEnd(bool force)
    {
        if (force) _stickToBottom = true;
        if (_stickToBottom) ScrollToBottomNow();
    }

    private void UpdateJumpButton() =>
        popup.JumpToLatestButton.Visibility = !_stickToBottom && popup.MessagesScroll.ScrollableHeight > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void ScrollToBottomNow()
    {
        var sv = popup.MessagesScroll;
        _autoScrolling = true;
        sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: false); // animated: no jolts while streaming
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;
        var sv = popup.MessagesScroll;
        var atBottom = sv.ScrollableHeight - sv.VerticalOffset < 24;
        if (_autoScrolling) { _autoScrolling = false; if (atBottom) { UpdateJumpButton(); return; } }
        _stickToBottom = atBottom; // user scrolled: follow only if they are back at the bottom
        UpdateJumpButton();
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static Paragraph Spacer() => new() { FontSize = 6, Margin = new Thickness(0) };

    private static void AddLines(Paragraph p, string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) p.Inlines.Add(new LineBreak());
            p.Inlines.Add(new Run { Text = lines[i] });
        }
    }

    private Button CopyButton(Func<string> text, string tooltip)
    {
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = Glyphs.Copy, FontSize = 12 }, new TextBlock { Text = "Copy", FontSize = 12 } } },
            Padding = new Thickness(8, 3, 8, 3), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
            Opacity = 0.75,
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => { if (ClipboardText.TrySet(text())) notices.Show("Copied.", InfoBarSeverity.Success); else notices.Show("The clipboard is busy; try again.", InfoBarSeverity.Warning); };
        return button;
    }

    /// <summary>The answer's "⋯" menu, kept out of the way: today it only holds "Report this answer" (opens the provider's page).</summary>
    private static Button MoreButton(ReportTarget report)
    {
        var item = new MenuFlyoutItem { Text = $"Report this answer to {report.Provider}…", Icon = new FontIcon { Glyph = Glyphs.Flag } };
        item.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(report.Page);
        var button = new Button
        {
            Content = new FontIcon { Glyph = Glyphs.More, FontSize = 12 },
            Padding = new Thickness(8, 3, 8, 3), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
            Opacity = 0.75, Flyout = new MenuFlyout { Items = { item } },
        };
        ToolTipService.SetToolTip(button, "More");
        return button;
    }

    /// <summary>Re-arms a DispatcherQueueTimer only when stopped (cheap Start() calls while streaming).</summary>
    private sealed class RenderTimer
    {
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;

        public RenderTimer(Microsoft.UI.Dispatching.DispatcherQueue queue, TimeSpan interval, Action tick)
        {
            _timer = queue.CreateTimer();
            _timer.Interval = interval;
            _timer.IsRepeating = true;
            _timer.Tick += (_, _) => tick();
        }

        public void Start() { if (!_timer.IsRunning) _timer.Start(); }
        public void SetInterval(TimeSpan interval) { if (_timer.Interval != interval) _timer.Interval = interval; }
        public void Stop() => _timer.Stop();
    }
}
