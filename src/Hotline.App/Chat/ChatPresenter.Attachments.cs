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
            else chip.Children.Add(new FontIcon { Glyph = "\uE8A5", FontSize = 16 });
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
}
