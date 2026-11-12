using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.App.Capture;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Hotline.App.Chat;

/// <summary>
/// What will be sent with the next message: the tray of attachments and its chips, and every way something gets
/// there — files, paste, drag and drop, screen captures, the selection from the app you came from — plus the OCR
/// step that turns images into text for models that can't see them.
/// </summary>
internal sealed class AttachmentPanel(PopupWindow popup, AttachmentTray tray, HotlineSettings settings, NoticeArea notices,
    AttachmentVisuals visuals, PanelTheme theme, UiTasks tasks, FileLog log)
{
    private static readonly AttachmentLimits Limits = new();
    private string? _selectionId;
    private bool _regionCaptureOpen;
    private bool _ocrMissingNoted;

    public IReadOnlyList<Attachment> Items => tray.Items;

    public void Initialize()
    {
        popup.Input.Paste += Input_Paste;
        popup.Root.DragOver += Root_DragOver;
        popup.Root.Drop += Root_Drop;
        popup.AttachFilesItem.Click += (_, _) => tasks.Run("pick files", PickFilesAsync);
        popup.CaptureWindowItem.Click += (_, _) => tasks.Run("capture window", () => CaptureAsync(window: true));
        popup.CaptureScreenItem.Click += (_, _) => tasks.Run("capture screen", () => CaptureAsync(window: false));
        popup.CaptureRegionItem.Click += (_, _) => tasks.Run("capture region", CaptureRegionAsync);
        popup.CaptureRegionButton.Click += (_, _) => tasks.Run("capture region", CaptureRegionAsync);
        popup.CaptureWindowButton.Click += (_, _) => tasks.Run("capture window", () => CaptureAsync(window: true));
        popup.CaptureScreenButton.Click += (_, _) => tasks.Run("capture screen", () => CaptureAsync(window: false));
        popup.CaptureRequested += window => tasks.Run("capture", () => CaptureAsync(window));
        popup.RegionCaptureRequested += () => tasks.Run("capture region", CaptureRegionAsync);
    }

    /// <summary>Everything in the tray, which is then empty.</summary>
    public IReadOnlyList<Attachment> TakeAll()
    {
        var all = tray.TakeAll();
        Refresh();
        return all;
    }

    // ---- sources ------------------------------------------------------------------------------

    private async Task PickFilesAsync()
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, popup.Hwnd);
        IReadOnlyList<StorageFile> files = [];
        using (popup.Modal())
        {
            popup.HideForDialog(); // the normal Windows dialog, not one boxed in by the small always-on-top panel
            try { files = await picker.PickMultipleFilesAsync(); }
            finally { popup.ShowPopup(readSelection: false); } // back from the dialog: never copy from whatever had focus
            await AddFilesAsync(files);
        }
    }

    private async Task CaptureAsync(bool window)
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
            notices.Show(Strings.Attach_WindowGone, InfoBarSeverity.Informational);
        var name = usedWindow ? $"window-{DateTime.Now:HHmmss}.png" : $"screen-{DateTime.Now:HHmmss}.png";
        Add(AttachmentFactory.FromBytes(name, "image/png", png, Limits));
    }

    /// <summary>Region capture: freeze the monitor under the mouse, let the user drag a rectangle, attach it.</summary>
    private async Task CaptureRegionAsync()
    {
        if (_regionCaptureOpen) return; // one picker at a time (e.g. the key pressed again while it's up)
        _regionCaptureOpen = true;
        try
        {
            var png = await popup.WithHiddenAsync(async () =>
            {
                var monitor = ScreenCapture.CursorMonitorRect();
                var bgra = ScreenCapture.GrabBgra(monitor);
                // The picker makes alpha opaque in place; EncodeBgraPngAsync ignores alpha.
                var picker = new RegionSelectWindow(bgra, monitor);
                if (await picker.SelectAsync() is not { } r) return null;
                var crop = Hotline.Core.Windowing.RegionMath.CropBgra(bgra, monitor.Width, monitor.Height, r);
                return await ImageProcessor.EncodeBgraPngAsync(crop, r.Width, r.Height, settings.Chat.MaxImagePixels);
            });
            if (png is not null) Add(AttachmentFactory.FromBytes($"region-{DateTime.Now:HHmmss}.png", "image/png", png, Limits));
        }
        finally { _regionCaptureOpen = false; }
    }

    private void Input_Paste(object sender, TextControlPasteEventArgs e)
    {
        DataPackageView content;
        try { content = Clipboard.GetContent(); }
        catch (Exception ex) { log.Error("clipboard unavailable", ex); return; } // busy clipboard: default paste
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            e.Handled = true;
            tasks.Run("paste files", async () => await AddStorageItemsAsync(await content.GetStorageItemsAsync()));
        }
        else if (content.Contains(StandardDataFormats.Text))
        {
            return; // text wins (Excel/Office copy text plus a bitmap): normal paste into the box
        }
        else if (content.Contains(StandardDataFormats.Bitmap))
        {
            e.Handled = true;
            tasks.Run("paste image", async () =>
                await AddBytesAsync($"pasted-{DateTime.Now:HHmmss}.png", "image/png", await BitmapBytesAsync(content)));
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) && !e.DataView.Contains(StandardDataFormats.Bitmap)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Strings.Attach_DropCaption;
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems)) await AddStorageItemsAsync(await e.DataView.GetStorageItemsAsync());
            else if (e.DataView.Contains(StandardDataFormats.Bitmap)) // an image dragged from a browser or viewer, no file
                await AddBytesAsync($"dropped-{DateTime.Now:HHmmss}.png", "image/png", await BitmapBytesAsync(e.DataView));
        }
        catch (Exception ex) { log.Error("drop failed", ex); notices.Show(L.F(Strings.Attach_DropFailed, ex.Message), InfoBarSeverity.Error); }
        finally { deferral.Complete(); }
    }

    private static async Task<byte[]> BitmapBytesAsync(DataPackageView content)
    {
        var reference = await content.GetBitmapAsync();
        using var stream = await reference.OpenReadAsync();
        AttachmentFactory.RequireBitmapSize(stream.Size);
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private async Task AddStorageItemsAsync(IReadOnlyList<IStorageItem> items)
    {
        var folders = items.OfType<StorageFolder>().Select(f => f.Name).ToList();
        if (folders.Count > 0) notices.Show(L.F(Strings.Attach_NoFolders, string.Join(", ", folders)), InfoBarSeverity.Warning);
        await AddFilesAsync(items.OfType<StorageFile>().ToList());
    }

    private async Task AddFilesAsync(IReadOnlyList<StorageFile> files)
    {
        foreach (var file in files)
        {
            try
            {
                var size = (await file.GetBasicPropertiesAsync()).Size;
                var maxInput = AttachmentFactory.IsImage(file.Name, file.ContentType)
                    ? AttachmentFactory.MaxBitmapInputBytes : (ulong)Limits.MaxImageBytes;
                if (size > maxInput)
                {
                    notices.Show(L.F(Strings.Attach_TooLarge, file.Name, size / (1024 * 1024), maxInput / (1024 * 1024)), InfoBarSeverity.Warning);
                    continue;
                }
                var buffer = await FileIO.ReadBufferAsync(file);
                await AddBytesAsync(file.Name, file.ContentType, buffer.ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
            {
                log.Error($"could not read {file.Name}", ex);
                notices.Show(L.F(Strings.Attach_Unreadable, file.Name), InfoBarSeverity.Warning);
            }
        }
    }

    /// <summary>A file's bytes as an attachment (images are normalized to PNG within the size limit).</summary>
    public async Task AddBytesAsync(string name, string? mime, byte[] data)
    {
        Attachment attachment;
        try
        {
            attachment = AttachmentFactory.IsImage(name, mime)
                ? await AttachmentFactory.FromImageAsync(name, data, Limits,
                    bytes => ImageProcessor.NormalizeAsync(bytes, settings.Chat.MaxImagePixels))
                : AttachmentFactory.FromBytes(name, string.IsNullOrEmpty(mime) ? null : mime, data, Limits);
        }
        catch (AttachmentRejectedException ex) { notices.Show(ex.Message, InfoBarSeverity.Warning); return; }
        catch (Exception ex) when (ex is ArgumentException or COMException) { notices.Show(L.F(Strings.Attach_NotImage, name), InfoBarSeverity.Warning); return; }
        Add(attachment);
    }

    /// <summary>The selection from the app you came from, as a chip; replaces the previous selection chip.</summary>
    public void AttachSelection(string text, string? app)
    {
        if (SelectionAttachment.Create(text, app) is not { } attachment) return;
        if (_selectionId is { } old)
        {
            if (tray.Items.FirstOrDefault(a => a.Id == old) is { } previous && previous.Data.AsSpan().SequenceEqual(attachment.Data)) return;
            tray.Remove(old);
            _selectionId = null;
        }
        if (Add(attachment)) _selectionId = attachment.Id;
        else Refresh();
    }

    public void AttachActionText(Hotline.Core.Activation.AppActionRequest action)
    {
        if (action.Text is not { } text) return;
        if (action.FromWindows) AttachSelection(text, null);
        else if (action.TextAttachment() is { } attachment) Add(attachment);
    }

    public void Consume(IReadOnlyList<Attachment> snapshot)
    {
        tray.Consume(snapshot);
        Refresh();
    }

    private bool Add(Attachment attachment)
    {
        try { tray.Add(attachment); }
        catch (AttachmentRejectedException ex) { notices.Show(ex.Message, InfoBarSeverity.Warning); return false; }
        Refresh();
        log.Info($"attachment added: {attachment.Kind} {attachment.Data.Length} bytes");
        return true;
    }

    // ---- chips --------------------------------------------------------------------------------

    public void Refresh()
    {
        popup.ChipsPanel.Children.Clear();
        foreach (var a in tray.Items)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            chip.Children.Add(a.Kind == AttachmentKind.Image ? visuals.Thumbnail(a.Data, 28) : visuals.FileTile(a.Name, 28));
            chip.Children.Add(new TextBlock { Text = a.Name, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            var remove = new Button
            {
                Content = new FontIcon { Glyph = Glyphs.Close, FontSize = 10 }, Padding = new Thickness(4), BorderThickness = new Thickness(0),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            };
            ToolTipService.SetToolTip(remove, Strings.Common_Remove);
            AutomationProperties.SetName(remove, L.F(Strings.Attachment_RemoveNamed, a.Name));
            var id = a.Id;
            remove.Click += (_, _) => { tray.Remove(id); Refresh(); };
            chip.Children.Add(remove);
            popup.ChipsPanel.Children.Add(new Border
            {
                Child = chip, Padding = new Thickness(6, 3, 2, 3), CornerRadius = new CornerRadius(8),
                Background = PanelTheme.Brush(theme.Tokens.SurfaceStrong),
            });
        }
        popup.ChipsPanel.Visibility = tray.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- OCR before sending ---------------------------------------------------------------------

    /// <summary>Prepare detached attachments from the send snapshot. Null stops the send and retains the tray.</summary>
    public async Task<IReadOnlyList<Attachment>?> PrepareForSendAsync(IReadOnlyList<Attachment> snapshot, bool takesImages)
    {
        var images = snapshot.Where(a => a.Kind == AttachmentKind.Image).ToList();
        var (run, keepImages) = OcrPlan.Decide(settings.Chat.Ocr, takesImages);
        if (images.Count == 0 || !run) return snapshot;
        if (OcrReader.TryCreateEngine() is not { } engine)
        {
            if (!_ocrMissingNoted || !takesImages)
                notices.Show(Strings.Attach_NoOcr, InfoBarSeverity.Warning);
            _ocrMissingNoted = true;
            return takesImages ? snapshot : null;
        }
        var prepared = new AttachmentTray(Limits);
        foreach (var attachment in snapshot) prepared.Add(attachment);
        foreach (var image in images)
        {
            string text;
            try { text = await OcrReader.RecognizeAsync(engine, image.Data); }
            catch (Exception ex) when (ex is ArgumentException or COMException)
            {
                log.Error($"OCR of {image.Name} failed", ex);
                notices.Show(L.F(Strings.Attach_OcrFailed, image.Name), InfoBarSeverity.Warning);
                if (!takesImages) return null;
                continue;
            }
            if (!keepImages) prepared.Remove(image.Id);
            try { prepared.Add(OcrPlan.TextAttachment(image.Name, text)); }
            catch (AttachmentRejectedException ex)
            {
                notices.Show(ex.Message, InfoBarSeverity.Warning);
                return null;
            }
            log.Info($"OCR: {text.Length} chars from {image.Name} ({(keepImages ? "with" : "instead of")} the image)");
        }
        return prepared.TakeAll();
    }
}
