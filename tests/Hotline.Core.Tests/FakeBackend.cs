using System.Runtime.CompilerServices;
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

/// <summary>Scriptable backend: yields the given deltas; optionally throws or waits for a gate.</summary>
public sealed class FakeBackend(params ChatDelta[] deltas) : IChatBackend
{
    public string Id => "fake";
    public string DisplayName => "Fake";
    public BackendCapabilities Capabilities { get; init; } = new(Images: true, TextFiles: true);
    public Exception? Throw { get; init; }
    public TaskCompletionSource? Gate { get; init; }
    /// <summary>When true, a gated stream ignores cancellation until the gate opens (slow teardown).</summary>
    public bool SlowCancel { get; init; }
    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        Calls.Add(conversation.ToList());
        foreach (var d in deltas)
        {
            yield return d;
            if (Gate is not null) { if (SlowCancel) { await Gate.Task; if (Throw is not null) throw Throw; ct.ThrowIfCancellationRequested(); } else await Gate.Task.WaitAsync(ct); }
        }
        if (Throw is not null) throw Throw;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
