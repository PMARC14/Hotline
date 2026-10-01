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
    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        Calls.Add(conversation.ToList());
        foreach (var d in deltas)
        {
            yield return d;
            if (Gate is not null) await Gate.Task.WaitAsync(ct);
        }
        if (Throw is not null) throw Throw;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
