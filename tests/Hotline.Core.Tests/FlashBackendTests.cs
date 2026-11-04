using System.Net;
using System.Text.Json.Nodes;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Processes;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class FlashBackendTests : TempDirTest
{
    private sealed class DelayedProcess : ILineProcess
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed;
        public bool HasExited => Disposed;
        public string StandardErrorTail => "";
        public Task WriteLineAsync(string line, CancellationToken ct) => Task.CompletedTask;
        public void CompleteInput() { }
        public async Task<string?> ReadLineAsync(CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task;
            ct.ThrowIfCancellationRequested();
            return null;
        }
        public Task WaitForExitAsync(TimeSpan timeout) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class Factory : ILineProcessFactory
    {
        public readonly List<DelayedProcess> Started = [];
        public readonly TaskCompletionSource SecondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailStart;
        public ILineProcess Start(string exe, IReadOnlyList<string> args, string workingDirectory)
        {
            if (FailStart) throw new InvalidOperationException("start returned false");
            var p = new DelayedProcess();
            Started.Add(p);
            if (Started.Count == 2) SecondStarted.TrySetResult();
            return p;
        }
    }

    private IChatBackend Backend(BackendType type, Factory factory) => BackendFactory.Create(
        new BackendProfile { Id = "cli", Type = type, CliPath = "cli.exe" },
        new BackendDeps(factory, new AgyWorkspace(Path.Combine(_dir, "agy"), TimeProvider.System), new FileLog(Path.Combine(_dir, "log")),
            _ => true, null, null, _ => "prompt", _dir))!;

    [Theory]
    [InlineData(BackendType.Antigravity)]
    [InlineData(BackendType.ClaudeCode)]
    [InlineData(BackendType.Codex)]
    public async Task Superseded_cli_turn_finishes_cleanup_before_the_next_turn_starts(BackendType type)
    {
        var factory = new Factory();
        await using var backend = Backend(type, factory);
        using var firstToken = new CancellationTokenSource();
        using var secondToken = new CancellationTokenSource();
        ChatMessage[] chat = [new("u", ChatRole.User, "hi", [], DateTimeOffset.UtcNow)];
        await using var first = backend.StreamAsync(chat, firstToken.Token).GetAsyncEnumerator();
        var firstRead = first.MoveNextAsync().AsTask();
        await factory.Started[0].Entered.Task;
        firstToken.Cancel();
        await using var second = backend.StreamAsync(chat, secondToken.Token).GetAsyncEnumerator();
        var secondRead = second.MoveNextAsync().AsTask();
        var startsWhileUnwinding = factory.Started.Count;
        factory.Started[0].Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRead);
        // Await the second process without relying on timing or a real child process.
        await factory.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(2, factory.Started.Count);
        await factory.Started[1].Entered.Task;
        var killedNewProcess = factory.Started[1].Disposed;
        secondToken.Cancel();
        factory.Started[1].Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondRead);
        Assert.Equal(1, startsWhileUnwinding);
        Assert.False(killedNewProcess);
    }

    [Fact]
    public async Task Codex_start_failure_is_mapped_to_a_backend_error()
    {
        await using var backend = Backend(BackendType.Codex, new Factory { FailStart = true });
        var error = await Assert.ThrowsAsync<BackendException>(async () =>
        {
            await foreach (var _ in backend.StreamAsync([new("u", ChatRole.User, "hi", [], DateTimeOffset.UtcNow)], TestContext.Current.CancellationToken)) { }
        });
        Assert.Equal(BackendErrorKind.NotInstalled, error.Kind);
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"ok\",\"padding\":\"" + new string('x', 300 * 1024) + "\"}"),
            });
    }

    [Fact]
    public async Task OpenWebUi_rejects_an_oversized_creation_reply()
    {
        using var http = new HttpClient(new Handler());
        var error = await Assert.ThrowsAsync<OpenWebUiException>(() => OpenWebUiChat.CreateAsync(http,
            new Uri("http://localhost:3000"), "key", new JsonObject(), TestContext.Current.CancellationToken));
        Assert.Equal(OpenWebUiError.BadReply, error.Error);
    }

    [Fact]
    public void Saving_metadata_ignores_an_unrelated_locked_legacy_temp_file()
    {
        Directory.CreateDirectory(_dir);
        var store = new HistoryStore(_dir, TimeProvider.System);
        using var locked = File.Open(Path.Combine(_dir, "chat.meta.json.tmp"), FileMode.Create, FileAccess.Write, FileShare.None);
        var meta = new HistoryStore.Meta("title", DateTimeOffset.UtcNow, "cli", null, null, 2);
        store.SaveMeta("chat", meta);
        Assert.Equal(meta, store.LoadMeta("chat"));
    }
}
