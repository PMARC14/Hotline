using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.App.Capture;
using Hotline.Core.Chat;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Hotline.App.Chat;

internal sealed partial class ChatHost
{
    private static readonly AttachmentLimits Limits = new();

    private partial async Task PickFilesAsync()
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, popup.Hwnd);
        IReadOnlyList<StorageFile> files;
        using (popup.Modal())
            files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
        {
            var buffer = await FileIO.ReadBufferAsync(file);
            await AddBytesAsync(file.Name, file.ContentType, buffer.ToArray());
        }
        popup.ShowPopup();
    }

    private partial async Task CaptureAsync(bool window)
    {
        var target = popup.PreviousForeground;
        var png = await popup.WithHiddenAsync(async () =>
        {
            var rect = window && ScreenCapture.TryGetWindowRect(target, out var w) ? w : ScreenCapture.MonitorRect(target);
            var bgra = ScreenCapture.GrabBgra(rect);
            return await ImageProcessor.EncodeBgraPngAsync(bgra, rect.Width, rect.Height, settings.Chat.MaxImagePixels);
        });
        var name = window ? $"window-{DateTime.Now:HHmmss}.png" : $"screen-{DateTime.Now:HHmmss}.png";
        await AddAttachmentAsync(AttachmentFactory.FromBytes(name, "image/png", png, Limits));
    }

    private partial async Task AddBytesAsync(string name, string? mime, byte[] data)
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
        catch (AttachmentRejectedException ex) { Toast(ex.Message); return; }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException) { Toast($"{name} couldn't be read as an image."); return; }
        await AddAttachmentAsync(attachment);
    }

    private async Task AddAttachmentAsync(Attachment attachment)
    {
        try { tray.Add(attachment); }
        catch (AttachmentRejectedException ex) { Toast(ex.Message); return; }
        var thumb = attachment.Kind == AttachmentKind.Image ? await ImageProcessor.ThumbnailDataUrlAsync(attachment.Data) : null;
        Post(new { type = "attachmentAdded", id = attachment.Id, name = attachment.Name, kind = attachment.Kind.ToString(), thumb });
        log.Info($"attachment added: {attachment.Kind} {attachment.Data.Length} bytes");
    }
}
