using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.App.Capture;
using Hotline.Core.Chat;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Hotline.App.Chat;

internal sealed partial class ChatPresenter
{
    private static readonly AttachmentLimits Limits = new();

    private partial async Task PickFilesAsync()
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, popup.Hwnd);
        IReadOnlyList<StorageFile> files = [];
        using (popup.Modal())
        {
            popup.HideForDialog(); // the normal Windows dialog, not one boxed in by the small always-on-top panel
            try { files = await picker.PickMultipleFilesAsync(); }
            finally { popup.ShowPopup(); }
            await AddFilesAsync(files);
        }
    }

    private partial async Task CaptureAsync(bool window)
    {
        var target = popup.PreviousForeground;
        var usedWindow = window && ScreenCapture.TryGetWindowRect(target, out _);
        var png = await popup.WithHiddenAsync(async () =>
        {
            var rect = usedWindow && ScreenCapture.TryGetWindowRect(target, out var w) ? w : ScreenCapture.MonitorRect(target);
            var bgra = ScreenCapture.GrabBgra(rect);
            return await ImageProcessor.EncodeBgraPngAsync(bgra, rect.Width, rect.Height, settings.Chat.MaxImagePixels);
        });
        if (window && !usedWindow)
            Notice("That window isn't available (closed or minimized), so the whole screen was captured.", InfoBarSeverity.Informational);
        var name = usedWindow ? $"window-{DateTime.Now:HHmmss}.png" : $"screen-{DateTime.Now:HHmmss}.png";
        await AddAttachmentAsync(AttachmentFactory.FromBytes(name, "image/png", png, Limits));
    }

    /// <summary>Region capture: freeze the monitor under the mouse, let the user drag a rectangle, attach it.</summary>
    private async Task CaptureRegionAsync()
    {
        if (_regionCaptureOpen) return; // one picker at a time (e.g. the key pressed again while it's up)
        _regionCaptureOpen = true;
        try { await CaptureRegionCoreAsync(); }
        finally { _regionCaptureOpen = false; }
    }

    private bool _regionCaptureOpen;

    private async Task CaptureRegionCoreAsync()
    {
        var png = await popup.WithHiddenAsync(async () =>
        {
            var monitor = Capture.ScreenCapture.CursorMonitorRect();
            var bgra = Capture.ScreenCapture.GrabBgra(monitor);
            var picker = new Capture.RegionSelectWindow((byte[])bgra.Clone(), monitor);
            var selection = await picker.SelectAsync();
            if (selection is not { } r) return null;
            var crop = Hotline.Core.Windowing.RegionMath.CropBgra(bgra, monitor.Width, monitor.Height, r);
            return await Capture.ImageProcessor.EncodeBgraPngAsync(crop, r.Width, r.Height, settings.Chat.MaxImagePixels);
        });
        if (png is null) return; // cancelled
        await AddAttachmentAsync(AttachmentFactory.FromBytes($"region-{DateTime.Now:HHmmss}.png", "image/png", png, Limits));
    }

    private partial void Input_Paste(object sender, TextControlPasteEventArgs e)
    {
        DataPackageView content;
        try { content = Clipboard.GetContent(); }
        catch (Exception ex) { log.Error("clipboard unavailable", ex); return; } // busy clipboard: default paste
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            e.Handled = true;
            Run("paste files", async () =>
            {
                var items = await content.GetStorageItemsAsync();
                var folders = items.OfType<StorageFolder>().Select(f => f.Name).ToList();
                if (folders.Count > 0) Notice($"Folders can not be attached ({string.Join(", ", folders)}).", InfoBarSeverity.Warning);
                await AddFilesAsync(items.OfType<StorageFile>().ToList());
            });
        }
        else if (content.Contains(StandardDataFormats.Text))
        {
            return; // text wins (Excel/Office copy text plus a bitmap): normal paste into the box
        }
        else if (content.Contains(StandardDataFormats.Bitmap))
        {
            e.Handled = true;
            Run("paste image", async () =>
            {
                var reference = await content.GetBitmapAsync();
                using var stream = await reference.OpenReadAsync();
                var bytes = new byte[stream.Size];
                using var reader = new DataReader(stream);
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
                await AddBytesAsync($"pasted-{DateTime.Now:HHmmss}.png", "image/png", bytes);
            });
        }
    }

    private partial void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) && !e.DataView.Contains(StandardDataFormats.Bitmap)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Attach to Hotline";
    }

    private async partial void Root_Drop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var folders = items.OfType<StorageFolder>().ToList();
                if (folders.Count > 0) Notice($"Folders can't be attached ({string.Join(", ", folders.Select(f => f.Name))}).", InfoBarSeverity.Warning);
                await AddFilesAsync(items.OfType<StorageFile>().ToList());
            }
        }
        catch (Exception ex) { log.Error("drop failed", ex); Notice($"Couldn't attach dropped items: {ex.Message}", InfoBarSeverity.Error); }
        finally { deferral.Complete(); }
    }

    private async Task AddFilesAsync(IReadOnlyList<StorageFile> files)
    {
        foreach (var file in files)
        {
            try
            {
                var size = (await file.GetBasicPropertiesAsync()).Size;
                if (size > (ulong)Limits.MaxImageBytes)
                {
                    Notice($"{file.Name} is too large ({size / (1024 * 1024)} MB; max {Limits.MaxImageBytes / (1024 * 1024)} MB).", InfoBarSeverity.Warning);
                    continue;
                }
                var buffer = await FileIO.ReadBufferAsync(file);
                await AddBytesAsync(file.Name, file.ContentType, buffer.ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
            {
                log.Error($"could not read {file.Name}", ex);
                Notice($"{file.Name} couldn't be read.", InfoBarSeverity.Warning);
            }
        }
    }

    private async Task AddBytesAsync(string name, string? mime, byte[] data)
    {
        Attachment attachment;
        try
        {
            attachment = AttachmentFactory.FromBytes(name, string.IsNullOrEmpty(mime) ? null : mime, data, Limits);
            if (attachment.Kind == AttachmentKind.Image)
            {
                var png = await ImageProcessor.NormalizeAsync(attachment.Data, settings.Chat.MaxImagePixels);
                attachment = attachment with { Data = png, MimeType = "image/png", Name = Path.ChangeExtension(attachment.Name, ".png") };
            }
        }
        catch (AttachmentRejectedException ex) { Notice(ex.Message, InfoBarSeverity.Warning); return; }
        catch (Exception ex) when (ex is ArgumentException or COMException) { Notice($"{name} couldn't be read as an image.", InfoBarSeverity.Warning); return; }
        await AddAttachmentAsync(attachment);
    }

    private async Task AddAttachmentAsync(Attachment attachment)
    {
        try { tray.Add(attachment); }
        catch (AttachmentRejectedException ex) { Notice(ex.Message, InfoBarSeverity.Warning); return; }
        RefreshChips();
        log.Info($"attachment added: {attachment.Kind} {attachment.Data.Length} bytes");
        await Task.CompletedTask;
    }

    private partial void RefreshChips()
    {
        popup.ChipsPanel.Children.Clear();
        foreach (var a in tray.Items)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            if (a.Kind == AttachmentKind.Image)
            {
                var image = new Image { Width = 28, Height = 28, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                _ = SetThumbnailAsync(image, a.Data);
                chip.Children.Add(image);
            }
            else chip.Children.Add(FileTypeTile(a.Name, size: 28));
            chip.Children.Add(new TextBlock { Text = a.Name, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            var remove = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 10 }, Padding = new Thickness(4), BorderThickness = new Thickness(0), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            ToolTipService.SetToolTip(remove, "Remove");
            var id = a.Id;
            remove.Click += (_, _) => { tray.Remove(id); RefreshChips(); };
            chip.Children.Add(remove);
            popup.ChipsPanel.Children.Add(new Border
            {
                Child = chip, Padding = new Thickness(6, 3, 2, 3), CornerRadius = new CornerRadius(8),
                Background = Brush(_tokens.SurfaceStrong),
            });
        }
        popup.ChipsPanel.Visibility = tray.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task SetThumbnailAsync(Image image, byte[] png)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            var bitmap = new BitmapImage { DecodePixelWidth = 56 };
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
        }
        catch (Exception ex) { log.Error("thumbnail failed", ex); } // chip still shows the name
    }

    /// <summary>Row of attachment previews for a sent message: image thumbnails, file-type tiles for the rest.</summary>
    private UIElement AttachmentStrip(IReadOnlyList<Attachment> attachments, double thumbSize)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var a in attachments)
        {
            FrameworkElement tile;
            if (a.Kind == AttachmentKind.Image)
            {
                var image = new Image { Width = thumbSize, Height = thumbSize, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                _ = SetThumbnailAsync(image, a.Data);
                tile = new Border { Child = image, CornerRadius = new CornerRadius(6) };
                var png = a.Data;
                var menu = new MenuFlyout();
                var copyImage = new MenuFlyoutItem { Text = "Copy image", Icon = new FontIcon { Glyph = "\uE8C8" } };
                copyImage.Click += (_, _) => CopyImage(png);
                menu.Items.Add(copyImage);
                tile.ContextFlyout = menu;
            }
            else tile = FileTypeTile(a.Name, thumbSize);
            ToolTipService.SetToolTip(tile, a.Name);
            row.Children.Add(tile);
        }
        return row;
    }

    /// <summary>A document icon with the file extension underneath (e.g. "MD", "CS").</summary>
    private FrameworkElement FileTypeTile(string name, double size)
    {
        var ext = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
        if (ext.Length == 0) ext = "FILE";
        if (ext.Length > 4) ext = ext[..4];
        var icon = new FontIcon { Glyph = "\uE8A5", FontSize = size * 0.5 };
        if (size < 40)
            return new Grid { Width = size, Height = size, Children = { icon } };
        var label = new TextBlock { Text = ext, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { icon, label } };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(6), Background = Brush(_tokens.SurfaceStrong), Child = stack,
        };
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
            Notice("Image copied.", InfoBarSeverity.Success);
        }
        catch (Exception ex) { log.Error("copy image failed", ex); Notice("The clipboard is busy; try again.", InfoBarSeverity.Warning); }
    }
}
