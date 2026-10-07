using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Interop.UIAutomationClient;

namespace Hotline.App.Interop;

internal static class ClipboardOwner
{
    private static readonly Lazy<nint> Window = new(Start);

    public static nint Hwnd => Window.Value;

    private static nint Start()
    {
        var ready = new TaskCompletionSource<nint>();
        var thread = new Thread(() =>
        {
            // The system STATIC class needs no window procedure of ours; HWND_MESSAGE (-3) keeps it invisible.
            var hwnd = ClipboardNative.CreateWindowEx(0, "STATIC", "Hotline clipboard", 0, 0, 0, 0, 0, -3, 0, 0, 0);
            ready.SetResult(hwnd);
            if (hwnd == 0) return;
            while (ClipboardNative.GetMessage(out var msg, 0, 0, 0) > 0)
            {
                ClipboardNative.TranslateMessage(ref msg);
                ClipboardNative.DispatchMessage(ref msg);
            }
        }) { IsBackground = true, Name = "Hotline clipboard owner" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.Result;
    }
}
