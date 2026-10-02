using System.Diagnostics;
using Hotline.Core.Chat;
using Hotline.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Hotline.App.Chat;

/// <summary>
/// The conversation is ONE selectable RichTextBlock, so a drag selects across messages, paragraphs, lists and code.
/// Every message owns a contiguous run of paragraphs; only the last message (the streaming answer) ever changes,
/// so updates remove and re-append paragraphs at the end. Code blocks are selectable text shaded with a
/// TextHighlighter; their copy button sits in a small header row.
/// </summary>
internal sealed partial class ChatPresenter
{
    private abstract class Entry;

    private sealed class UserEntry(ChatMessage message) : Entry
    {
        public ChatMessage Message { get; } = message;
    }

    private sealed class AssistantView : Entry
    {
        public required string BackendName { get; init; }
        public IReadOnlyList<MdBlock> Blocks { get; set; } = [];
        /// <summary>Per rendered markdown block: how many paragraphs it added and its shading highlighters.</summary>
        public List<(int Paragraphs, List<TextHighlighter> Shades)> Rendered { get; } = [];
        public Paragraph? CaretParagraph { get; set; }
        public string Text { get; set; } = "";
        public string? Error { get; set; }
        public bool Streaming { get; set; } = true;
        public bool Dirty { get; set; }
    }

    private readonly Dictionary<string, AssistantView> _assistants = [];
    private readonly List<Entry> _entries = [];
    private RichTextBlock _transcript = null!;

    private void CreateTranscript()
    {
        _transcript = new RichTextBlock
        {
            IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap,
            FontSize = _tokens.FontSizePx, FontFamily = _style.Font,
        };
        popup.MessagesPanel.Children.Clear();
        popup.MessagesPanel.Children.Add(_transcript);
    }

    private double ContentWidth => Math.Max(200, popup.MessagesPanel.ActualWidth - popup.MessagesPanel.Padding.Left - popup.MessagesPanel.Padding.Right - 4);

    private void ClearTranscript()
    {
        _entries.Clear();
        _assistants.Clear();
        _transcript.Blocks.Clear();
        _transcript.TextHighlighters.Clear();
    }

    /// <summary>Re-renders everything (font/theme changes).</summary>
    private void RebuildTranscript()
    {
        _transcript.FontSize = _tokens.FontSizePx;
        _transcript.FontFamily = _style.Font;
        _transcript.Blocks.Clear();
        _transcript.TextHighlighters.Clear();
        foreach (var entry in _entries)
        {
            if (entry is UserEntry u) AppendUser(u.Message);
            else if (entry is AssistantView a)
            {
                a.Blocks = [];
                a.Rendered.Clear();
                a.CaretParagraph = null;
                AppendAssistantHeader(a);
                RenderAssistant(a);
                if (!a.Streaming) AppendAssistantFooter(a);
            }
        }
    }

    // ---- user messages ------------------------------------------------------------------------

    private void AddUser(ChatMessage message)
    {
        _entries.Add(new UserEntry(message));
        AppendUser(message);
        ScrollToEnd(force: true);
    }

    private void AppendUser(ChatMessage message)
    {
        _transcript.Blocks.Add(Spacer());
        if (message.Text.Length > 0)
        {
            var p = new Paragraph { TextAlignment = TextAlignment.Right, Margin = new Thickness(80, 0, 0, 4) };
            AddLines(p, message.Text);
            _transcript.Blocks.Add(p);
            Shade(p, Brush(_tokens.UserBubble));
        }
        if (message.Attachments.Count > 0)
        {
            var p = new Paragraph { TextAlignment = TextAlignment.Right, Margin = new Thickness(80, 0, 0, 4) };
            p.Inlines.Add(new InlineUIContainer { Child = AttachmentStrip(message.Attachments, thumbSize: 72) });
            _transcript.Blocks.Add(p);
        }
    }

    // ---- answers -------------------------------------------------------------------------------

    private void StartAssistant(string id, string backendName)
    {
        var view = new AssistantView { BackendName = backendName, Dirty = true };
        _assistants[id] = view;
        _entries.Add(view);
        AppendAssistantHeader(view);
        _renderTimer?.Start();
    }

    private void AppendAssistantHeader(AssistantView view)
    {
        _transcript.Blocks.Add(Spacer());
        if (view.BackendName.Length == 0) return;
        var label = new Paragraph { FontSize = 11, Foreground = _style.Muted, Margin = new Thickness(0, 0, 0, 2) };
        label.Inlines.Add(new Run { Text = view.BackendName });
        _transcript.Blocks.Add(label);
    }

    private void Finish(string id)
    {
        if (_assistants.TryGetValue(id, out var view))
        {
            view.Dirty = true;
            RenderDirty();
            view.Streaming = false;
            RemoveCaret(view);
            AppendAssistantFooter(view);
        }
        SetBusy(false);
    }

    private void ShowError(string id, string message)
    {
        if (!_assistants.TryGetValue(id, out var view)) return;
        view.Error = message;
        if (_entries.Count > 0 && _entries[^1] == view) AppendError(view);
        ScrollToEnd(force: false);
    }

    private void AppendAssistantFooter(AssistantView view)
    {
        if (view.Text.Length > 0)
        {
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
            p.Inlines.Add(new InlineUIContainer { Child = CopyButton(() => view.Text, "Copy response") });
            _transcript.Blocks.Add(p);
        }
        if (view.Error is not null) AppendError(view);
    }

    private void AppendError(AssistantView view)
    {
        var bar = new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Error, Message = view.Error, Width = ContentWidth };
        var retry = new Button { Content = "Retry" };
        retry.Click += (_, _) => { bar.IsOpen = false; Run("retry", chat.RetryAsync); };
        bar.ActionButton = retry;
        var p = new Paragraph();
        p.Inlines.Add(new InlineUIContainer { Child = bar });
        _transcript.Blocks.Add(p);
    }

    /// <summary>
    /// Re-renders changed answers. Only blocks from the first changed one onward are rebuilt (earlier paragraphs stay
    /// put, keeping selection), and the interval adapts to how long rendering takes.
    /// </summary>
    private void RenderDirty()
    {
        var watch = Stopwatch.StartNew();
        var any = false;
        foreach (var view in _assistants.Values.Where(v => v.Dirty))
        {
            view.Dirty = false;
            any = true;
            if (_entries.Count > 0 && _entries[^1] == view && view.Streaming) RenderAssistant(view);
        }
        if (!any) { _renderTimer?.Stop(); return; }
        _renderTimer?.SetInterval(RenderThrottle.NextInterval(watch.Elapsed));
        ScrollToEnd(force: false);
    }

    /// <summary>Brings the (last) answer's paragraphs up to date with its text.</summary>
    private void RenderAssistant(AssistantView view)
    {
        var blocks = MarkdownModel.Parse(view.Text);
        var from = MarkdownModel.FirstChangedIndex(view.Blocks, blocks);
        RemoveCaret(view);
        while (view.Rendered.Count > from)
        {
            var (paragraphs, shades) = view.Rendered[^1];
            view.Rendered.RemoveAt(view.Rendered.Count - 1);
            foreach (var h in shades) _transcript.TextHighlighters.Remove(h);
            for (var k = 0; k < paragraphs; k++) _transcript.Blocks.RemoveAt(_transcript.Blocks.Count - 1);
        }
        for (var b = from; b < blocks.Count; b++)
        {
            var shaded = new List<Paragraph>();
            var count = MarkdownRenderer.AppendBlock(_transcript, blocks[b], _style, ContentWidth, shaded);
            view.Rendered.Add((count, shaded.Select(p => Shade(p, _style.CodeBackground)).ToList()));
        }
        view.Blocks = blocks;
        if (view.Streaming)
        {
            view.CaretParagraph = new Paragraph();
            view.CaretParagraph.Inlines.Add(new Run { Text = "▍", Foreground = _style.Accent });
            _transcript.Blocks.Add(view.CaretParagraph);
        }
    }

    private void RemoveCaret(AssistantView view)
    {
        if (view.CaretParagraph is null) return;
        _transcript.Blocks.Remove(view.CaretParagraph);
        view.CaretParagraph = null;
    }

    // ---- helpers -----------------------------------------------------------------------------

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

    /// <summary>Shades a paragraph's text range (code blocks, your messages). Must be called after it is in the transcript.</summary>
    private TextHighlighter Shade(Paragraph p, Brush background)
    {
        var start = p.ContentStart.Offset;
        var highlighter = new TextHighlighter { Background = background };
        highlighter.Ranges.Add(new TextRange { StartIndex = start, Length = Math.Max(0, p.ContentEnd.Offset - start) });
        _transcript.TextHighlighters.Add(highlighter);
        return highlighter;
    }

    /// <summary>
    /// Debug self-test: streams a scripted answer (lists, code, table, quote) through the real transcript code while the
    /// panel stays hidden, logs what was built, then clears it. Exercises rendering without typing into the panel.
    /// </summary>
    public void SelfTest()
    {
        const string answer = "# Heading\n\nSome **bold** text with `code` and <kbd>Ctrl</kbd>.\n\n- one\n- two\n  1. nested\n\n" +
            "```csharp\nvar x = 1;\nConsole.WriteLine(x);\n```\n\n| a | b |\n|---|---|\n| 1 | 22 |\n\n> quoted\n\n---\n\nEnd.";
        try
        {
            OnChatEvent(new ConversationReset());
            OnChatEvent(new UserMessageAdded(new ChatMessage("st-u", ChatRole.User, "self test\nsecond line", [], DateTimeOffset.Now)));
            OnChatEvent(new AssistantStarted("st-a", "Self test"));
            for (var i = 0; i < answer.Length; i += 17)
            {
                OnChatEvent(new AssistantDelta("st-a", answer.Substring(i, Math.Min(17, answer.Length - i)), false));
                RenderDirty();
            }
            OnChatEvent(new AssistantCompleted("st-a"));
            OnChatEvent(new AssistantStarted("st-b", "Self test"));
            OnChatEvent(new AssistantFailed("st-b", BackendErrorKind.Failed, "simulated failure"));
            log.Info($"selftest ok: {_transcript.Blocks.Count} paragraphs, {_transcript.TextHighlighters.Count} shaded ranges, " +
                     $"{_transcript.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineUIContainer>().Count()} embedded controls");
            ApplyAppearance(); // full rebuild path
            log.Info($"selftest rebuild ok: {_transcript.Blocks.Count} paragraphs, {_transcript.TextHighlighters.Count} shaded ranges");
        }
        catch (Exception ex) { log.Error("selftest FAILED", ex); }
        finally { OnChatEvent(new ConversationReset()); }
    }
}
