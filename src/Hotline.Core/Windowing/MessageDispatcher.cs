using Hotline.Core.Diagnostics;

namespace Hotline.Core.Windowing;

/// <summary>
/// Routes window messages to handlers. Handlers run inside a native window procedure, where an
/// escaping exception terminates the process, so every handler call is contained and logged.
/// </summary>
public sealed class MessageDispatcher(FileLog log)
{
    private readonly Dictionary<uint, Func<nint, nint, bool>> _handlers = [];

    public void On(uint msg, Func<nint, nint, bool> handler) => _handlers[msg] = handler;

    /// <returns>true if a handler consumed the message.</returns>
    public bool Dispatch(uint msg, nint wParam, nint lParam)
    {
        if (!_handlers.TryGetValue(msg, out var handler))
            return false;
        try
        {
            return handler(wParam, lParam);
        }
        catch (Exception ex)
        {
            log.Error($"handler for message 0x{msg:X} failed", ex);
            return false;
        }
    }
}
