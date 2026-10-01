namespace Hotline.App.Interop;

/// <summary>Subclasses a window once and dispatches selected messages to handlers (true = handled).</summary>
internal sealed class WindowMessageHook
{
    private readonly Native.SubclassProc _proc; // field keeps the delegate alive for native code
    private readonly Dictionary<uint, Func<nint, nint, bool>> _handlers = [];

    public WindowMessageHook(nint hwnd)
    {
        Hwnd = hwnd;
        _proc = Proc;
        if (!Native.SetWindowSubclass(hwnd, _proc, 1, 0))
            throw new InvalidOperationException("SetWindowSubclass failed");
    }

    public nint Hwnd { get; }

    public void On(uint msg, Func<nint, nint, bool> handler) => _handlers[msg] = handler;

    private nint Proc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
        => _handlers.TryGetValue(msg, out var handler) && handler(wParam, lParam)
            ? 0
            : Native.DefSubclassProc(hWnd, msg, wParam, lParam);
}
