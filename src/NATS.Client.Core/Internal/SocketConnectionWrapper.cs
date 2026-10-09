namespace NATS.Client.Core.Internal;

// wraps INatsSocketConnection and signals on disconnect
internal record SocketConnectionWrapper(INatsSocketConnection InnerSocket) : INatsSocketConnection
{
    private readonly TaskCompletionSource _waitForClosedSource =
#if NETSTANDARD
        new(TaskCreationOptions.None);
#else
        new();
#endif

    public Task WaitForClosed => _waitForClosedSource.Task;

    // Called from the read loop. Must not block: disposing a WebSocket completes the
    // pending receive while DisposeAsync is still awaiting the close, and on a
    // single-threaded host (Blazor WebAssembly) blocking here would deadlock.
    // TrySet* is atomic, so whichever of this and DisposeAsync runs first wins.
    public void SignalDisconnected(Exception exception) => _waitForClosedSource.TrySetObservedException(exception);

    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer) => InnerSocket.SendAsync(buffer);

    public ValueTask<int> ReceiveAsync(Memory<byte> buffer) => InnerSocket.ReceiveAsync(buffer);

    public async ValueTask DisposeAsync()
    {
        _waitForClosedSource.TrySetCanceled();
        await InnerSocket.DisposeAsync().ConfigureAwait(false);
    }
}
