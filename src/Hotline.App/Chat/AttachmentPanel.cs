using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.App.Capture;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml;
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
    private readonly HashSet<string> _ocrDone = [];

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
            finally { popup.ShowPopup(); }
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
            notices.Show("That window isn't available (closed or minimized), so the whole screen was captured.", InfoBarSeverity.Informational);
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
                var picker = new RegionSelectWindow((byte[])bgra.Clone(), monitor);
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

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) && !e.DataView.Contains(StandardDataFormats.Bitmap)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Attach to Hotline";
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems)) await AddStorageItemsAsync(await e.DataView.GetStorageItemsAsync());
        }
        catch (Exception ex) { log.Error("drop failed", ex); notices.Show($"Couldn't attach dropped items: {ex.Message}", InfoBarSeverity.Error); }
        finally { deferral.Complete(); }
    }

    private async Task AddStorageItemsAsync(IReadOnlyList<IStorageItem> items)
    {
        var folders = items.OfType<StorageFolder>().Select(f => f.Name).ToList();
        if (folders.Count > 0) notices.Show($"Folders can't be attached ({string.Join(", ", folders)}).", InfoBarSeverity.Warning);
        await AddFilesAsync(items.OfType<StorageFile>().ToList());
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
                    notices.Show($"{file.Name} is too large ({size / (1024 * 1024)} MB; max {Limits.MaxImageBytes / (1024 * 1024)} MB).", InfoBarSeverity.Warning);
                    continue;
                }
                var buffer = await FileIO.ReadBufferAsync(file);
                await AddBytesAsync(file.Name, file.ContentType, buffer.ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
            {
                log.Error($"could not read {file.Name}", ex);
                notices.Show($"{file.Name} couldn't be read.", InfoBarSeverity.Warning);
            }
        }
    }

    /// <summary>A file's bytes as an attachment (images are normalized to PNG within the size limit).</summary>
    public async Task AddBytesAsync(string name, string? mime, byte[] data)
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
        catch (AttachmentRejectedException ex) { notices.Show(ex.Message, InfoBarSeverity.Warning); return; }
        catch (Exception ex) when (ex is ArgumentException or COMException) { notices.Show($"{name} couldn't be read as an image.", InfoBarSeverity.Warning); return; }
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
            ToolTipService.SetToolTip(remove, "Remove");
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

    /// <summary>
    /// Applies chat.ocr to the tray before a send: images become (or gain) a "Text in …" attachment. False when the
    /// send should stop (a notice says why).
    /// </summary>
    public async Task<bool> PrepareForSendAsync(bool takesImages)
    {
        var images = tray.Items.Where(a => a.Kind == AttachmentKind.Image).ToList();
        if (images.Count == 0) return true;
        var (run, keepImages) = OcrPlan.Decide(settings.Chat.Ocr, takesImages);
        if (!run) return true;
        if (OcrReader.TryCreateEngine() is not { } engine)
        {
            if (!_ocrMissingNoted || !takesImages)
                notices.Show("Windows has no OCR language installed, so text in images can't be read. Add one in Settings › Time & language › " +
                             "Language & region (a language with \"Optical character recognition\").", InfoBarSeverity.Warning);
            _ocrMissingNoted = true;
            return takesImages; // an image model still gets the image; a text-only one can't take it
        }
        var ok = true;
        try
        {
            foreach (var image in images)
            {
                if (_ocrDone.Contains(image.Id))
                {
                    // Already read on an earlier try to send; its text is attached. A text-only model now: drop the image.
                    if (!keepImages) { tray.Remove(image.Id); _ocrDone.Remove(image.Id); }
                    continue;
                }
                string text;
                try { text = await OcrReader.RecognizeAsync(engine, image.Data); }
                catch (Exception ex) when (ex is ArgumentException or COMException)
                {
                    log.Error($"OCR of {image.Name} failed", ex);
                    notices.Show($"Couldn't read the text in {image.Name}.", InfoBarSeverity.Warning);
                    ok &= takesImages; // an image model still gets the image; carry on with the others
                    continue;
                }
                var textAttachment = OcrPlan.TextAttachment(image.Name, text);
                if (!keepImages) tray.Remove(image.Id); // replacing: the count stays the same
                try { tray.Add(textAttachment); }
                catch (AttachmentRejectedException ex)
                {
                    if (!keepImages) tray.Add(image); // put it back: a rejection never loses the image
                    notices.Show(ex.Message, InfoBarSeverity.Warning);
                    ok = false;
                    break;
                }
                if (keepImages) _ocrDone.Add(image.Id);
                log.Info($"OCR: {text.Length} chars from {image.Name} ({(keepImages ? "with" : "instead of")} the image)");
            }
        }
        finally { Refresh(); }
        return ok;
    }
}
