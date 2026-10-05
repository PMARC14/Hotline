using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.Core.Windowing;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Hotline.App.Chat;

/// <summary>Reads the text in an image with Windows' built-in OCR (offline, the user's OCR languages).</summary>
internal static class OcrReader
{
    /// <summary>Null when Windows has no OCR language installed.</summary>
    public static OcrEngine? TryCreateEngine() => OcrEngine.TryCreateFromUserProfileLanguages();

    public static async Task<string> RecognizeAsync(OcrEngine engine, byte[] image)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(image.AsBuffer());
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
}
