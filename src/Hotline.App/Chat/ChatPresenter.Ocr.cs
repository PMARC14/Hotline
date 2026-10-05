using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.Core.Chat;
using Hotline.Core.Windowing;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Hotline.App.Chat;

/// <summary>
/// Screenshot text (chat.ocr): before sending, images are read with Windows' built-in OCR (offline) and sent as text
/// to connections whose model can't take images (or always, alongside the image).
/// </summary>
internal sealed partial class ChatPresenter
{
    private bool _ocrMissingNoted;

    /// <summary>Applies the OCR plan to the tray. False when sending should stop (the notice explains why).</summary>
    private async Task<bool> ApplyOcrAsync()
    {
        var images = tray.Items.Where(a => a.Kind == AttachmentKind.Image).ToList();
        if (images.Count == 0) return true;
        var takesImages = chat.CurrentCapabilities?.Images ?? true;
        var (run, keepImages) = OcrPlan.Decide(settings.Chat.Ocr, takesImages);
        if (!run) return true;
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            if (!_ocrMissingNoted || !takesImages)
                Notice("Windows has no OCR language installed, so text in images can't be read. Add one in Settings › Time & language › " +
                       "Language & region (a language with \"Optical character recognition\").", InfoBarSeverity.Warning);
            _ocrMissingNoted = true;
            return takesImages; // an image model still gets the image; a text-only one can't take it
        }
        var ok = true;
        foreach (var image in images)
        {
            if (_ocrDone.Contains(image.Id)) continue; // already read on an earlier try to send (Always keeps the image)
            string text;
            try { text = await RecognizeAsync(engine, image.Data); }
            catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
            {
                log.Error($"OCR of {image.Name} failed", ex);
                Notice($"Couldn't read the text in {image.Name}.", InfoBarSeverity.Warning);
                ok &= takesImages; // an image model still gets the image; carry on with the others
                continue;
            }
            try { tray.Add(OcrPlan.TextAttachment(image.Name, text)); } // add first: a rejection never loses the image
            catch (AttachmentRejectedException ex) { Notice(ex.Message, InfoBarSeverity.Warning); ok = false; break; }
            if (keepImages) _ocrDone.Add(image.Id);
            else tray.Remove(image.Id);
            log.Info($"OCR: {text.Length} chars from {image.Name} ({(keepImages ? "with" : "instead of")} the image)");
        }
        RefreshChips();
        return ok;
    }

    private readonly HashSet<string> _ocrDone = [];

    private static async Task<string> RecognizeAsync(OcrEngine engine, byte[] data)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(data.AsBuffer());
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);
        // The engine has a size limit; scale large screenshots down to it.
        var (w, h) = ImageMath.FitWithin((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight, (int)OcrEngine.MaxImageDimension);
        var transform = new BitmapTransform { ScaledWidth = (uint)w, ScaledHeight = (uint)h, InterpolationMode = BitmapInterpolationMode.Fant };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join("\n", result.Lines.Select(l => l.Text));
    }

    /// <summary>Self-test: OCR the rendered (off-screen) transcript and check its heading is read back.</summary>
    private async Task SelfTestOcrAsync()
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null) { log.Info("selftest ocr: no OCR language installed"); return; }
            var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await rtb.RenderAsync(popup.MessagesPanel);
            var pixels = (await rtb.GetPixelsAsync()).ToArray();
            var png = await Capture.ImageProcessor.EncodeBgraPngAsync(pixels, rtb.PixelWidth, rtb.PixelHeight, 4096);
            var text = await RecognizeAsync(engine, png);
            log.Info($"selftest ocr: {(text.Contains("Heading", StringComparison.OrdinalIgnoreCase) ? "ok" : "MISSING heading")} ({text.Length} chars)");
        }
        catch (Exception ex) { log.Error("selftest ocr FAILED", ex); }
    }
}
