using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Hotline.App.Chat;

/// <summary>How attachments look: image thumbnails, file-type tiles, and the preview row under a sent message.</summary>
internal sealed class AttachmentVisuals(PanelTheme theme, NoticeArea notices, FileLog log)
{
    /// <summary>Row of previews for a sent message: thumbnails (right-click: Copy image), file-type tiles for the rest.</summary>
    public UIElement Strip(IReadOnlyList<Attachment> attachments, double thumbSize)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var a in attachments)
        {
            FrameworkElement tile;
            if (a.Kind == AttachmentKind.Image)
            {
                tile = new Border { Child = Thumbnail(a.Data, thumbSize), CornerRadius = new CornerRadius(6) };
                var png = a.Data;
                var copyImage = new MenuFlyoutItem { Text = "Copy image", Icon = new FontIcon { Glyph = Glyphs.Copy } };
                copyImage.Click += (_, _) => CopyImage(png);
                tile.ContextFlyout = new MenuFlyout { Items = { copyImage } };
            }
            else tile = FileTile(a.Name, thumbSize);
            ToolTipService.SetToolTip(tile, a.Name);
            row.Children.Add(tile);
        }
        return row;
    }

    public Image Thumbnail(byte[] png, double size)
    {
        var image = new Image { Width = size, Height = size, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
        _ = LoadAsync(image, png, (int)Math.Ceiling(size * 2));
        return image;
    }

    /// <summary>A document icon, with the file extension underneath when there's room (e.g. "MD", "CS").</summary>
    public FrameworkElement FileTile(string name, double size)
    {
        var ext = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
        if (ext.Length == 0) ext = "FILE";
        if (ext.Length > 4) ext = ext[..4];
        var icon = new FontIcon { Glyph = Glyphs.Document, FontSize = size * 0.5 };
        if (size < 40) return new Grid { Width = size, Height = size, Children = { icon } };
        var label = new TextBlock { Text = ext, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(6), Background = PanelTheme.Brush(theme.Tokens.SurfaceStrong),
            Child = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { icon, label } },
        };
    }

    private async Task LoadAsync(Image image, byte[] png, int decodeWidth)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            var bitmap = new BitmapImage { DecodePixelWidth = decodeWidth };
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
        }
        catch (Exception ex) { log.Error("thumbnail failed", ex); } // the name still shows
    }

    private void CopyImage(byte[] png)
    {
        try
        {
            var stream = new InMemoryRandomAccessStream();
            stream.WriteAsync(png.AsBuffer()).AsTask().GetAwaiter().GetResult();
            stream.Seek(0);
            var package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            Clipboard.SetContent(package);
            notices.Show("Image copied.", InfoBarSeverity.Success);
        }
        catch (Exception ex) { log.Error("copy image failed", ex); notices.Show("The clipboard is busy; try again.", InfoBarSeverity.Warning); }
    }
}
