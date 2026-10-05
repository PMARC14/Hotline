using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.App.Chat;

/// <summary>
/// Scripted conversations through the real panel code, with no AI call: <see cref="Demo"/> (shown, for screenshots)
/// and <see cref="RunAsync"/> (hotline://selftest: drawn off-screen, never visible, then cleared), which also checks
/// OCR on the rendered transcript, a rate-limit note and the "/" suggestion list.
/// </summary>
internal sealed class PanelSelfTest(PopupWindow popup, TranscriptView transcript, Composer composer, HotlineSettings settings, FileLog log,
    Action<ChatEvent> apply, Action rebuild, Action newChat)
{
    private const string DemoAnswer = "### Area of a circle\n\nThe area is\n\n$$A = \\pi r^2$$\n\n" +
            "- **r** is the radius (half the diameter)\n- **π** ≈ 3.14159\n\n" +
            "For a radius of 2: $A = \\pi \\times 2^2 \\approx 12.57$.\n\n" +
            "```python\nimport math\n\ndef circle_area(r: float) -> float:\n    return math.pi * r ** 2\n\nprint(round(circle_area(2), 2))  # 12.57\n```\n\n" +
            "| Radius | Area |\n|---|---|\n| 1 | 3.14 |\n| 2 | 12.57 |\n| 3 | 28.27 |";

    private const string TestAnswer = "# Heading\n\nSome **bold** text with `code` and <kbd>Ctrl</kbd>.\n\n- one\n- two\n  1. nested\n\n" +
            "```csharp\nvar x = 1;\nConsole.WriteLine(x);\n```\n\n| a | b |\n|---|---|\n| 1 | 22 |\n\n> quoted\n\n$$\\pi r^2$$\n\n<div align=\"center\">\n\n<svg viewBox=\"0 0 10 10\" width=\"40\" height=\"40\">\n\n<circle cx=\"5\" cy=\"5\" r=\"4\" fill=\"#0284c7\"/>\n</svg>\n\n</div>\n\n- [x] done ==marked== x^2^\n\n---\n\nEnd.";

    public void Demo()
    {
        try
        {
            newChat(); // a real reset (stops any answer, clears the controller), not just the view
            popup.ShowPopup();
            apply(new UserMessageAdded(new ChatMessage("demo-u", ChatRole.User, "How do I find the area of a circle? Show it in Python.", [], DateTimeOffset.Now)));
            apply(new AssistantStarted("demo-a", settings.Chat.Backends.FirstOrDefault()?.Name ?? "Gemini (Antigravity)"));
            apply(new AssistantDelta("demo-a", DemoAnswer, false));
            apply(new AssistantCompleted("demo-a"));
            log.Info("demo conversation shown");
        }
        catch (Exception ex) { log.Error("demo failed", ex); }
    }

    public async Task RunAsync()
    {
        if (popup.IsShown) popup.HidePopup(); // test-only link: render off-screen, never in front of the user
        try
        {
            popup.ShowOffscreen();
            async Task Step(string name)
            {
                log.Info($"selftest step: {name}");
                await Task.Delay(120); // let the frame be laid out and drawn
            }
            apply(new ConversationReset());
            await Step("reset");
            apply(new UserMessageAdded(new ChatMessage("st-u", ChatRole.User, "self test\nsecond line", [], DateTimeOffset.Now)));
            await Step("user message");
            apply(new AssistantStarted("st-a", "Self test"));
            await Step("answer started");
            for (var i = 0; i < TestAnswer.Length; i += 23)
            {
                apply(new AssistantDelta("st-a", TestAnswer.Substring(i, Math.Min(23, TestAnswer.Length - i)), false));
                transcript.RenderDirty();
                await Step($"delta {i}");
            }
            apply(new AssistantCompleted("st-a"));
            await Step("completed");
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
            var draft = composer.Text;
            composer.Text = "/";
            await Step("quick actions");
            log.Info($"selftest quick actions: {composer.SuggestionCount} suggestion(s)");
            composer.Text = draft;
        }
        catch (Exception ex) { log.Error("selftest FAILED", ex); }
        finally
        {
            apply(new ConversationReset());
            popup.EndOffscreen();
        }
    }

    /// <summary>OCR the rendered (off-screen) transcript and check its heading is read back.</summary>
    private async Task CheckOcrAsync()
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
