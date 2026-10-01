using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.Core.Windowing;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Hotline.App.Capture;

/// <summary>Decode/scale/encode images with Windows.Graphics.Imaging (no extra dependencies). Output is always PNG.</summary>
internal static class ImageProcessor
{
    public static async Task<byte[]> NormalizeAsync(byte[] image, int maxPx)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(image.AsBuffer());
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);
        var (w, h) = ImageMath.FitWithin((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight, maxPx);
        var transform = new BitmapTransform { ScaledWidth = (uint)w, ScaledHeight = (uint)h, InterpolationMode = BitmapInterpolationMode.Fant };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        return await EncodeAsync(bitmap, w, h);
    }

    public static async Task<byte[]> EncodeBgraPngAsync(byte[] bgra, int width, int height, int maxPx)
    {
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(bgra.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
        var (w, h) = ImageMath.FitWithin(width, height, maxPx);
        return await EncodeAsync(bitmap, w, h);
    }

    public static async Task<string> ThumbnailDataUrlAsync(byte[] png, int px = 96)
        => "data:image/png;base64," + Convert.ToBase64String(await NormalizeAsync(png, px));

    private static async Task<byte[]> EncodeAsync(SoftwareBitmap bitmap, int outWidth, int outHeight)
    {
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        if (outWidth != bitmap.PixelWidth || outHeight != bitmap.PixelHeight)
        {
            encoder.BitmapTransform.ScaledWidth = (uint)outWidth;
            encoder.BitmapTransform.ScaledHeight = (uint)outHeight;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
        await encoder.FlushAsync();
        var bytes = new byte[output.Size];
        using var reader = new DataReader(output.GetInputStreamAt(0));
        await reader.LoadAsync((uint)output.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
