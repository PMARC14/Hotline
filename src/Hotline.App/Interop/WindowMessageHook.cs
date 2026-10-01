using Hotline.Core.Diagnostics;
using Hotline.Core.Windowing;

namespace Hotline.App.Interop;

/// <summary>Subclasses a window once and routes selected messages through a guarded <see cref="MessageDispatcher"/>.</summary>
internal sealed class WindowMessageHook
{
    private readonly Native.SubclassProc _proc; // field keeps the delegate alive for native code
    private readonly MessageDispatcher _dispatcher;

    public WindowMessageHook(nint hwnd, FileLog log)
    {
        Hwnd = hwnd;
        _dispatcher = new MessageDispatcher(log);
        _proc = Proc;
        if (!Native.SetWindowSubclass(hwnd, _proc, 1, 0))
            throw new InvalidOperationException("SetWindowSubclass failed");
    }

    public nint Hwnd { get; }

    public void On(uint msg, Func<nint, nint, bool> handler) => _dispatcher.On(msg, handler);

    private nint Proc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
        => _dispatcher.Dispatch(msg, wParam, lParam) ? 0 : Native.DefSubclassProc(hWnd, msg, wParam, lParam);
}
