using Hotline.Core.Diagnostics;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class MessageDispatcherTests : IDisposable
{
    private readonly string _log = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"), "h.log");
    public void Dispose() { var d = Path.GetDirectoryName(_log)!; if (Directory.Exists(d)) Directory.Delete(d, recursive: true); }

    [Fact]
    public void Unregistered_message_is_not_handled()
        => Assert.False(new MessageDispatcher(new FileLog(_log)).Dispatch(0x1234, 0, 0));

    [Fact]
    public void Registered_handler_receives_params_and_result_is_returned()
    {
        var d = new MessageDispatcher(new FileLog(_log));
        (nint w, nint l) seen = default;
        d.On(0x8001, (w, l) => { seen = (w, l); return true; });
        Assert.True(d.Dispatch(0x8001, 2, 7));
        Assert.Equal((2, 7), seen);
    }

    [Fact]
    public void Throwing_handler_is_contained_logged_and_reported_unhandled()
    {
        var d = new MessageDispatcher(new FileLog(_log));
        d.On(0x8001, (_, _) => throw new InvalidOperationException("boom in handler"));
        Assert.False(d.Dispatch(0x8001, 0, 0));
        Assert.Contains("boom in handler", File.ReadAllText(_log));
    }
}
