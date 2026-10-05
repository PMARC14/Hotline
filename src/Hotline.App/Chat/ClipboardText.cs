using Windows.ApplicationModel.DataTransfer;

namespace Hotline.App.Chat;

/// <summary>Puts text on the clipboard (false when another app holds it).</summary>
internal static class ClipboardText
{
    public static bool TrySet(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { return false; }
    }
}
