using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.App.Chat;

/// <summary>
/// Scripted conversations through the real panel code, with no AI call: <see cref="DemoAsync"/> (shown, for screenshots)
/// and <see cref="RunAsync"/> (hotline://selftest: drawn off-screen, never visible, then cleared), which also checks
/// OCR on the rendered transcript, a rate-limit note and the "/" suggestion list.
/// </summary>
internal sealed class PanelSelfTest(PopupWindow popup, TranscriptView transcript, Composer composer, HotlineSettings settings, FileLog log,
    Action<ChatEvent> apply, Action rebuild, Action newChat, Action<string, string?> attachSelection, Func<IReadOnlyList<Attachment>> takeAttachments)
{
    // The demo conversations are interface text (Strings.Demo_*), so the Store screenshots can be taken in every language.
    private static string DemoAnswer => Strings.Demo_MathAnswer;

    private const string TestAnswer = "# Heading\n\nSome **bold** text with `code` and <kbd>Ctrl</kbd>.\n\n- one\n- two\n  1. nested\n\n" +
            "```csharp\nvar x = 1;\nConsole.WriteLine(x);\n```\n\n| a | b |\n|---|---|\n| 1 | 22 |\n\n> quoted\n\n$$\\pi r^2$$\n\n<div align=\"center\">\n\n<svg viewBox=\"0 0 10 10\" width=\"40\" height=\"40\">\n\n<circle cx=\"5\" cy=\"5\" r=\"4\" fill=\"#0284c7\"/>\n</svg>\n\n</div>\n\n- [x] done ==marked== x^2^\n\n---\n\nEnd.";

    private static string SummaryText => Strings.Demo_SummaryText;

    private static string SummaryAnswer => Strings.Demo_SummaryAnswer;

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
                    await TypeAsync(Strings.Demo_SummaryQuestion);
                    await Task.Delay(8000); // the Store screenshot is taken here: chip + question (long enough for every language: a short question is typed in half a second)
                    composer.Text = "";
                    apply(new UserMessageAdded(new ChatMessage("demo-u", ChatRole.User, Strings.Demo_SummaryQuestion, takeAttachments(), DateTimeOffset.Now)));
                    await StreamAsync(backend, SummaryAnswer);
                    break;
                case "slash":
                    await TypeAsync("/"); // opens the quick-action list
                    break;
                default:
                    await TypeAsync(Strings.Demo_MathQuestion);
                    composer.Text = "";
                    apply(new UserMessageAdded(new ChatMessage("demo-u", ChatRole.User, Strings.Demo_MathQuestion, [], DateTimeOffset.Now)));
                    await StreamAsync(backend, DemoAnswer);
                    // The answer is taller than the panel and the view follows the stream to the bottom; the screenshot reads
                    // better from the top (the question, the heading and the equation).
                    await Task.Delay(400);
                    popup.MessagesScroll.ChangeView(null, 0, null, disableAnimation: true);
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

    /// <summary>The user's bar with its items moved: <paramref name="top"/> above the message box, <paramref name="bottom"/> below (left groups).</summary>
    private static ToolbarModel Rows(ToolbarModel bar, IReadOnlyList<string> top, IReadOnlyList<string> bottom) => bar with
    {
        Placed = new Dictionary<Quadrant, IReadOnlyList<string>>
        {
            [Quadrant.TopLeft] = top, [Quadrant.TopRight] = [], [Quadrant.BottomLeft] = bottom, [Quadrant.BottomRight] = [],
        },
    };

    public async Task RunAsync()
    {
        // The URI handler is async void; setup and restoration can fail too.
        try { await RunCoreAsync(); }
        catch (Exception ex) { log.Error("selftest FAILED during setup or restoration", ex); }
    }

    private async Task RunCoreAsync()
    {
        if (popup.IsShown) popup.HidePopup(); // test-only link: render off-screen, never in front of the user
        // What the test changes, restored in finally whatever happens: the user's draft, button rows and direction.
        var userDraft = composer.Text;
        var userBar = popup.ToolbarModel;
        var userUp = popup.GrowsUp;
        Action? stopCounting = null;
        var errorsBefore = log.ErrorCount;
        var expectedErrors = 0; // errors the self-test provokes on purpose (the simulated failed answer logs one)
        var checks = 0;
        try
        {
            popup.ShowOffscreen();
            // Rendering health: frames drawn and layout cycles during the test. A layout cycle can leave the window
            // undrawable for good (every later open is blank), so either one fails the test.
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
                checks++;
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
            expectedErrors++;
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
            // Narrower than the button row (the condition of the layout cycle), then back.
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
            var all = Enum.GetValues<Quadrant>().SelectMany(userBar.Items).ToList();
            popup.ApplyToolbar(Rows(userBar, top: all, bottom: []));
            await Step("buttons above");
            await SnapshotAsync("6-buttons-above");
            popup.ApplyToolbar(Rows(userBar, top: all.Take(all.Count / 2).ToList(), bottom: all.Skip(all.Count / 2).ToList()));
            await Step("buttons split");
            await SnapshotAsync("7-buttons-split");
            PanelLayout.Apply(popup, up: false);
            await Step("buttons split, down");
            await SnapshotAsync("8-buttons-split-down");
            PanelLayout.Apply(popup, userUp);
            popup.ApplyToolbar(userBar);
            await Step("buttons restored");
            // Bar looks, the ⋯ menu and a damaged file, from models resolved in memory (the user's toolbar.json is never touched).
            const string builtIn = "\"pin\", \"captureWindow\", \"captureScreen\", \"captureRegion\", \"prompt\", \"recent\", \"continueIn\"";
            static string Bar(string style, string moreLeft = "", string buttons = "", string overrides = "") => $$"""
                { "version": 2, "top": { "left": ["effort", "model", "provider"], "right": [] },
                  "items": { "left": [{{builtIn}}{{moreLeft}}], "right": ["newChat", "settings"] },
                  "style": {{style}}, "overrides": { {{overrides}} }, "buttons": { {{buttons}} } }
                """;
            async Task Look(string name, string json, int width)
            {
                popup.ResizeOffscreen(width);
                popup.ApplyToolbar(ToolbarModel.Resolve(json));
                await Step(name);
                await Task.Delay(200);
                CheckGrid(name);
                await SnapshotAsync(name);
            }
            // The cell grid: every row that has something in it is exactly one cell (BaseHeight) high whatever the sizes, and a picker
            // is as tall as a button (one cell less the inset the renderer gave it).
            void CheckGrid(string name)
            {
                foreach (var (row, label) in new[] { (popup.TopToolbar, "top"), (popup.Toolbar, "bottom") })
                    if (row.Visibility == Microsoft.UI.Xaml.Visibility.Visible && row.Children.Count > 0 && Math.Abs(row.ActualHeight - ToolbarPlan.BaseHeight) > 0.5)
                        log.Error($"selftest FAILED: {name}: the {label} button row is {row.ActualHeight} high, not {ToolbarPlan.BaseHeight}");
                foreach (var (box, id) in new[] { (popup.EffortBox, "effort"), (popup.ModelBox, "model"), (popup.ProviderBox, "provider") })
                    if (box.Visibility == Microsoft.UI.Xaml.Visibility.Visible && box.ActualHeight > 0 && Math.Abs(box.ActualHeight - ToolbarPlan.Inner(ToolbarPlan.BaseHeight)) > 0.5)
                        log.Error($"selftest FAILED: {name}: the {id} picker is {box.ActualHeight} high, a button is {ToolbarPlan.Inner(ToolbarPlan.BaseHeight)}");
                // The outline is the bar's look for the picker: 1.5 DIP drawn inside its height, none when it has no outline.
                foreach (var (box, id) in new[] { (popup.EffortBox, "effort"), (popup.ModelBox, "model"), (popup.ProviderBox, "provider") })
                {
                    var wanted = popup.ToolbarModel.EffectiveStyle(id).Outline == true ? 1.5 : 0;
                    if (Math.Abs(box.BorderThickness.Left - wanted) > 0.01)
                        log.Error($"selftest FAILED: {name}: the {id} picker's border is {box.BorderThickness.Left}, not {wanted}");
                }
            }
            var customButtons = string.Join(", ", Enumerable.Range(1, 24).Select(i => $$"""
                "c{{i}}": { "label": "Custom {{i}}", "icon": "E734", "text": "t{{i}}" }
                """));
            var customIds = string.Concat(Enumerable.Range(1, 24).Select(i => $", \"c{i}\""));
            await Look("9-wide-outline", Bar("""{ "width": "wide", "outline": true }"""), 720);
            await Look("10-compact-filled", Bar("""{ "width": "compact", "filled": true }"""), 720);
            // 24 custom buttons: at the narrowest width the left ones go into the ⋯ menu; wider they come back (no layout cycle either way).
            await Look("11-custom-720", Bar("{}", customIds, customButtons), 720);
            await Look("12-custom-320", Bar("{}", customIds, customButtons), 320);
            await Look("13-custom-1000", Bar("{}", customIds, customButtons), 1000);
            await Look("14-custom-320-again", Bar("{}", customIds, customButtons), 320);
            // A hand-damaged file: a repeated key and wrong values still give a drawn bar.
            await Look("15-damaged-file", Bar("""{ "width": "huge", "outline": "yes" }""").Replace("\"version\": 2", "\"version\": 2, \"version\": 2"), 720);
            // Wide (two cells across, one cell high) at the narrowest width: the left buttons go into the ⋯ menu (one cell); then mixed widths.
            await Look("16-wide-320", Bar("""{ "width": "wide", "outline": true }"""), 320);
            await Look("17-mixed-widths-720", Bar("""{ "width": "normal" }""", overrides: """ "pin": { "width": "compact" }, "prompt": { "width": "wide" } """), 720);
            // Custom buttons at their widest (4 cells) on a wide bar: still one cell high (ToolbarPlan.Metrics).
            var biggest = string.Join(", ", Enumerable.Range(1, 24).Select(i => $$""" "c{{i}}": { "width": 4 } """));
            await Look("18-custom-4-cells-320", Bar("""{ "width": "wide" }""", customIds, customButtons, biggest), 320);
            // A coloured outline bar-wide, and one item in another colour.
            await Look("19-outline-colour", Bar("""{ "width": "wide", "outline": true, "outlineColor": "#E81123" }""", overrides: """ "pin": { "outlineColor": "#0078D4" } """), 720);
            // Items in the left, centre and right groups of both rows.
            await Look("20-centre-group", """
                { "version": 2, "top": { "left": ["effort", "model", "provider"], "center": ["recent"], "right": ["continueIn"] },
                  "items": { "left": ["pin", "captureWindow", "captureScreen"], "center": ["captureRegion", "prompt"], "right": ["newChat", "settings"] },
                  "style": { "outline": true } }
                """, 720);
            // The reference layouts (ToolbarProfiles; the unit tests run all of them at many widths): the ones that mix the pickers
            // into the bottom row, the extremes and the damaged file at 680 DIP, the picker-mixing ones again at 320, then the
            // separated pickers.
            var profileNumber = 0;
            async Task Profile(string name, int width) => await Look($"P{++profileNumber:00}-{name}-{width}", ToolbarProfiles.Named(name).Json, width);
            string[] mixing =
            [
                "pickers-bottom-left-before", "pickers-bottom-left-after", "pickers-bottom-left-between", "pickers-bottom-centre",
                "pickers-bottom-centre-crowded", "pickers-bottom-right", "custom-24-pickers-bottom",
            ];
            string[] others =
            [
                "default", "pickers-top-right", "pickers-top-centre-both-rows", "spans-all-1", "spans-all-8", "spans-mixed", "all-compact", "all-wide",
                "custom-24-wide-pickers-bottom-centre", "only-pickers", "all-items-in-one-group", "empty-bar", "damaged-file", "mixed-button-sizes",
            ];
            foreach (var name in mixing.Concat(others)) await Profile(name, 680);
            foreach (var name in mixing) await Profile(name, 320);
            // The pickers placed one by one (P29-P31): in two areas, between buttons, one per row.
            foreach (var name in new[] { "pickers-apart-corners", "picker-between-buttons", "pickers-one-per-row-right" }) await Profile(name, 680);
            // The more button and its box (P32-P34): five user entries, the row's overflow merged into the box, a crowded top row beside a more row.
            foreach (var name in new[] { "more-menu-bottom-right-with-5-items", "more-menu-top-centre-with-overflow", "more-in-both-rows" }) await Profile(name, 680);
            // The shipped width (11 buttons, 460 DIP) too: the default layout and the width extremes there (P35-P45).
            foreach (var name in mixing.Concat(["default", "all-compact", "all-wide", "mixed-button-sizes"])) await Profile(name, (int)ToolbarPlan.WidthForButtons(11));
            // The dropdowns take the bar's outline and fill (P46, P47): checked by CheckGrid (their height, their border) and by eye in the snapshots.
            foreach (var name in new[] { "outlined-pickers", "filled-outlined-bar" }) await Profile(name, 680);
            popup.ApplyToolbar(userBar);
            popup.ResizeOffscreen(720);
            await Step("looks restored");
            _renderCheck?.Invoke();
            _renderCheck = null;
        }
        catch (Exception ex) { log.Error("selftest FAILED", ex); }
        finally
        {
            stopCounting?.Invoke();
            _renderCheck = null;
            ShowGrips?.Invoke(false);
            if (!popup.ToolbarModel.Equals(userBar)) popup.ApplyToolbar(userBar);
            PanelLayout.Apply(popup, userUp);
            composer.Text = userDraft;
            newChat();
            popup.EndOffscreen();
            // The one marker tests/smoke/smoke.ps1 waits for: always written last, whatever failed (every failure logged an error).
            log.Info($"selftest complete: {checks} checks, {Math.Max(0, log.ErrorCount - errorsBefore - expectedErrors)} failed");
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
