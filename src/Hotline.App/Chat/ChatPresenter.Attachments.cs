using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Chat;

internal sealed partial class ChatPresenter
{
    private partial Task PickFilesAsync() { Notice("Attaching files arrives in the next step.", InfoBarSeverity.Informational); return Task.CompletedTask; }
    private partial Task CaptureAsync(bool window) { Notice("Capture arrives in the next step.", InfoBarSeverity.Informational); return Task.CompletedTask; }
    private partial void Input_Paste(object sender, TextControlPasteEventArgs e) { }
    private partial void Root_DragOver(object sender, DragEventArgs e) { }
    private partial void Root_Drop(object sender, DragEventArgs e) { }
    private partial void RefreshChips() => popup.ChipsPanel.Visibility = tray.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
}
