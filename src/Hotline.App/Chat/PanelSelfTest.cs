using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.App.Chat;

/// <summary>
/// Scripted conversations through the real panel code, with no AI call: <see cref="DemoAsync"/> (shown, for screenshots)
/// and <see cref="RunAsync"/> (hotline://selftest: drawn off-screen, never visible, then cleared), which also checks
/// OCR on the rendered transcript, a rate-limit note and the "/" suggestion list.
/// </summary>
internal sealed class PanelSelfTest(PopupWindow popup, TranscriptView transcript, Composer composer, HotlineSettings settings, FileLog log,
    Action<ChatEvent> apply, Action rebuild, Action newChat, Action<string, string?> attachSelection, Func<IReadOnlyList<Attachment>> takeAttachments)
{
    private const string DemoAnswer = "### Area of a circle\n\nThe area is\n\n$$A = \\pi r^2$$\n\n" +
            "- **r** is the radius (half the diameter)\n- **π** ≈ 3.14159\n\n" +
            "For a radius of 2: $A = \\pi \\times 2^2 \\approx 12.57$.\n\n" +
            "```python\nimport math\n\ndef circle_area(r: float) -> float:\n    return math.pi * r ** 2\n\nprint(round(circle_area(2), 2))  # 12.57\n```\n\n" +
            "| Radius | Area |\n|---|---|\n| 1 | 3.14 |\n| 2 | 12.57 |\n| 3 | 28.27 |";

    private const string TestAnswer = "# Heading\n\nSome **bold** text with `code` and <kbd>Ctrl</kbd>.\n\n- one\n- two\n  1. nested\n\n" +
            "```csharp\nvar x = 1;\nConsole.WriteLine(x);\n```\n\n| a | b |\n|---|---|\n| 1 | 22 |\n\n> quoted\n\n$$\\pi r^2$$\n\n<div align=\"center\">\n\n<svg viewBox=\"0 0 10 10\" width=\"40\" height=\"40\">\n\n<circle cx=\"5\" cy=\"5\" r=\"4\" fill=\"#0284c7\"/>\n</svg>\n\n</div>\n\n- [x] done ==marked== x^2^\n\n---\n\nEnd.";

    private const string SummaryText = "Remote work has reshaped how teams communicate. Asynchronous updates now replace many " +
            "meetings, written decisions travel further than spoken ones, and teams that document well onboard new people in " +
            "days instead of weeks. The trade-off is fewer chance conversations, so many companies now plan in-person time on " +
            "purpose: a few days each quarter for planning, mentoring and getting to know each other.";

    private const string SummaryAnswer = """
        - **Async first:** written updates replace many meetings, and written decisions reach more people.
        - **Documentation pays off:** well-documented teams onboard new people in days, not weeks.
        - **Planned together-time:** chance conversations are rarer, so companies schedule in-person days each quarter.
        """;

    /// <summary>
    /// hotline://demo[?scene=math|summary|slash]: a scripted conversation shown in the real panel and streamed like a
    /// real answer, for Store screenshots and the trailer. No AI is called.
    /// </summary>
    public async Task DemoAsync(string? scene)
    {
        try
        {
            newChat(); // a real reset (stops any answer, clears the controller), not just the view
            popup.ShowPopup();
            var backend = settings.Chat.Backends.FirstOrDefault()?.Name ?? "Gemini (Antigravity)";
            switch (scene?.ToLowerInvariant())
            {
                case "summary":
                    attachSelection(SummaryText, "Microsoft Edge"); // the chip, as when the key opens over a selection
                    await TypeAsync("Summarize this in three bullet points.");
                    await Task.Delay(2500); // the Store screenshot is taken here: chip + question
                    composer.Text = "";
                    apply(new UserMessageAdded(new ChatMessage("demo-u", ChatRole.User, "Summarize this in three bullet points.", takeAttachments(), DateTimeOffset.Now)));
                    await StreamAsync(backend, SummaryAnswer);
                    break;
                case "slash":
                    await TypeAsync("/"); // opens the quick-action list
                    break;
                default:
                    await TypeAsync("How do I find the area of a circle? Show it in Python.");
                    composer.Text = "";
                    apply(new UserMessageAdded(new ChatMessage("demo-u", ChatRole.User, "How do I find the area of a circle? Show it in Python.", [], DateTimeOffset.Now)));
                    await StreamAsync(backend, DemoAnswer);
                    break;
            }
            log.Info($"demo conversation shown ({scene ?? "math"})");
        }
        catch (Exception ex) { log.Error("demo failed", ex); }
    }

    /// <summary>Fills the message box a few characters at a time, as if typed (only the box: no keys are sent).</summary>
    private async Task TypeAsync(string text)
    {
        for (var i = 1; i <= text.Length; i += 2)
        {
            composer.Text = text[..i];
            await Task.Delay(35);
        }
        composer.Text = text;
        await Task.Delay(400);
    }

    private async Task StreamAsync(string backend, string answer)
    {
        apply(new AssistantStarted("demo-a", backend));
        await Task.Delay(500);
        for (var i = 0; i < answer.Length; i += 12)
        {
            apply(new AssistantDelta("demo-a", answer.Substring(i, Math.Min(12, answer.Length - i)), false));
            await Task.Delay(30);
        }
        apply(new AssistantCompleted("demo-a"));
    }

    public async Task RunAsync()
    {
        if (popup.IsShown) popup.HidePopup(); // test-only link: render off-screen, never in front of the user
        // What the test changes, restored in finally whatever happens: the user's draft, button rows and direction.
        var userDraft = composer.Text;
        var (userBottom, userTop) = (popup.ToolbarItems.ToList(), popup.TopToolbarItems.ToList());
        var userUp = popup.GrowsUp;
        Action? stopCounting = null;
        try
        {
            popup.ShowOffscreen();
            // Rendering health: frames drawn and layout cycles during the test. A layout cycle can leave the window
            // undrawable for good (2026-10-17: every later open was blank), so either one fails the test.
            var cyclesBefore = App.LayoutCycles;
            var frames = 0;
            void OnFrame(object? s, object e) => frames++;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFrame;
            stopCounting = () => Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFrame;
            _renderCheck = () =>
            {
                stopCounting();
                var cycles = App.LayoutCycles - cyclesBefore;
                if (cycles > 0) log.Error($"selftest FAILED: {cycles} layout cycle(s) during the test (the window may stop drawing)");
                if (frames < 10) log.Error($"selftest FAILED: only {frames} frame(s) drawn (the window isn't rendering)");
                else log.Info($"selftest rendering ok: {frames} frames, no layout cycle");
            };
            async Task Step(string name)
            {
                log.Info($"selftest step: {name}");
                await Task.Delay(120); // let the frame be laid out and drawn
            }
            newChat(); // a real reset (stops any answer, clears the controller), as the demo does
            await Step("reset");
            apply(new UserMessageAdded(new ChatMessage("st-u", ChatRole.User, "self test\nsecond line", [], DateTimeOffset.Now)));
            await Step("user message");
            apply(new AssistantStarted("st-a", "Self test"));
            await Step("answer started");
            // No text yet: the caret becomes the thinking spinner (a ProgressRing in the transcript; drawn, so a
            // rendering crash would show here), then the first text turns it back into the bar.
            for (var waited = 0; waited < 3000 && !transcript.IsThinking; waited += 100) await Task.Delay(100);
            await Step($"thinking spinner shown={transcript.IsThinking}");
            // Only when it's on and due within the wait (chat.thinkingIndicator / chat.thinkingDelayMs).
            if (!transcript.IsThinking && settings.Chat.ThinkingIndicator && settings.Chat.ThinkingDelayMs < 2500)
                log.Error("selftest FAILED: the thinking spinner didn't show");
            for (var i = 0; i < TestAnswer.Length; i += 23)
            {
                apply(new AssistantDelta("st-a", TestAnswer.Substring(i, Math.Min(23, TestAnswer.Length - i)), false));
                transcript.RenderDirty();
                await Step($"delta {i}");
                if (i == 0 && transcript.IsThinking) log.Error("selftest FAILED: the thinking spinner stayed after text came");
            }
            apply(new AssistantCompleted("st-a"));
            await Step("completed");
            await SnapshotAsync("1-answer-up");
            await CheckOcrAsync();
            apply(new AssistantStarted("st-b", "Self test"));
            apply(new AssistantFailed("st-b", BackendErrorKind.Failed, "simulated failure"));
            await Step("failed answer");
            log.Info($"selftest ok: {transcript.ParagraphCount} paragraphs, {transcript.EmbeddedControlCount} embedded controls");
            rebuild(); // the full rebuild path (theme/font change)
            await Step("rebuild");
            log.Info($"selftest rebuild ok: {transcript.ParagraphCount} paragraphs");
            apply(new AssistantStatus("st-b", "Self test is rate limited, retrying in 0 s…"));
            await Step("status note");
            composer.Text = "/";
            await Step("quick actions");
            log.Info($"selftest quick actions: {composer.SuggestionCount} suggestion(s)");
            composer.Text = "draft kept through a swap";
            var up = popup.GrowsUp;
            PanelLayout.Apply(popup, up: false); // the chat growing down from the bar: rendered like the rest
            await Step("layout down");
            await SnapshotAsync("2-answer-down");
            var keptDown = composer.Text == "draft kept through a swap";
            PanelLayout.Apply(popup, up: true);
            await Step("layout up");
            if (!keptDown || composer.Text != "draft kept through a swap") log.Error("selftest FAILED: the draft changed in a layout swap");
            else log.Info("selftest layout swap ok");
            PanelLayout.Apply(popup, up); // back to the panel's own direction
            var barBefore = popup.BarStack.ActualHeight;
            double barWithGrips;
            ShowGrips?.Invoke(true); // the Alt grips, through the same code as Alt (placed for the direction)
            try { await Step("grips shown"); barWithGrips = popup.BarStack.ActualHeight; await SnapshotAsync("3-alt-grips"); }
            finally { ShowGrips?.Invoke(false); }
            if (Math.Abs(barWithGrips - barBefore) > 0.5) log.Error($"selftest FAILED: the grips changed the layout ({barBefore} -> {barWithGrips})");
            else log.Info($"selftest grips ok ({popup.GripLayer.Children.Count} handles)");
            // Narrower than the button row (the condition of the 2026-10-17 layout cycle), then back.
            popup.ResizeOffscreen(360);
            await Step("narrow panel");
            await Task.Delay(400);
            await SnapshotAsync("4-narrow");
            popup.ResizeOffscreen(1000);
            await Step("wide panel");
            await SnapshotAsync("5-wide");
            popup.ResizeOffscreen(720);
            await Step("normal width");
            // The button rows: all above the message box, then split across both, then back to the user's own.
            var all = userTop.Concat(userBottom).ToList();
            popup.ApplyToolbar([], all);
            await Step("buttons above");
            await SnapshotAsync("6-buttons-above");
            popup.ApplyToolbar(all.Skip(all.Count / 2).ToList(), all.Take(all.Count / 2).ToList());
            await Step("buttons split");
            await SnapshotAsync("7-buttons-split");
            PanelLayout.Apply(popup, up: false);
            await Step("buttons split, down");
            await SnapshotAsync("8-buttons-split-down");
            PanelLayout.Apply(popup, userUp);
            popup.ApplyToolbar(userBottom, userTop);
            await Step("buttons restored");
            _renderCheck?.Invoke();
            _renderCheck = null;
        }
        catch (Exception ex) { log.Error("selftest FAILED", ex); }
        finally
        {
            stopCounting?.Invoke();
            _renderCheck = null;
            ShowGrips?.Invoke(false);
            if (!popup.ToolbarItems.SequenceEqual(userBottom) || !popup.TopToolbarItems.SequenceEqual(userTop)) popup.ApplyToolbar(userBottom, userTop);
            PanelLayout.Apply(popup, userUp);
            composer.Text = userDraft;
            newChat();
            popup.EndOffscreen();
        }
    }

    /// <summary>OCR the rendered (off-screen) transcript and check its heading is read back.</summary>
    private Action? _renderCheck;

    /// <summary>
    /// What the panel looked like at a step, as a PNG in logs\selftest\ (for a review by eye or by a vision model).
    /// Gives up after 5 s: the snapshot must never hang the test.
    /// </summary>
    private async Task SnapshotAsync(string name)
    {
        try
        {
            async Task Save()
            {
                var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                await rtb.RenderAsync(popup.Root);
                var pixels = (await rtb.GetPixelsAsync()).ToArray();
                var png = await Capture.ImageProcessor.EncodeBgraPngAsync(pixels, rtb.PixelWidth, rtb.PixelHeight, 4096);
                var dir = Directory.CreateDirectory(Path.Combine(SnapshotDirectory, "selftest")).FullName;
                await File.WriteAllBytesAsync(Path.Combine(dir, name + ".png"), png);
            }
            await Save().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) { log.Error($"selftest FAILED: snapshot {name}", ex); }
    }

    /// <summary>Shows or hides the Alt grips as Alt does (set by the presenter).</summary>
    public Action<bool>? ShowGrips { get; set; }

    /// <summary>Where snapshots go (the logs folder).</summary>
    public string SnapshotDirectory { get; set; } = Path.GetTempPath();

    private async Task CheckOcrAsync()
    {
        try { await CheckOcrCoreAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException) { log.Error("selftest FAILED: the OCR check got no image or text in 10 s (the window may not be rendering)"); }
    }

    private async Task CheckOcrCoreAsync()
    {
        try
        {
            if (OcrReader.TryCreateEngine() is not { } engine) { log.Info("selftest ocr: no OCR language installed"); return; }
            var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await rtb.RenderAsync(popup.MessagesPanel);
            var pixels = (await rtb.GetPixelsAsync()).ToArray();
            var png = await Capture.ImageProcessor.EncodeBgraPngAsync(pixels, rtb.PixelWidth, rtb.PixelHeight, 4096);
            var text = await OcrReader.RecognizeAsync(engine, png);
            log.Info($"selftest ocr: {(text.Contains("Heading", StringComparison.OrdinalIgnoreCase) ? "ok" : "MISSING heading")} ({text.Length} chars)");
        }
        catch (Exception ex) { log.Error("selftest ocr FAILED", ex); }
    }
}
