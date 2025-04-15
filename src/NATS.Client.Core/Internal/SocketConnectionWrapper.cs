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

    private readonly SemaphoreSlim _sem = new(1);

    public Task WaitForClosed => _waitForClosedSource.Task;

    public void SignalDisconnected(Exception exception)
    {
        // guard with semaphore so this doesn't race DisposeAsync
        _sem.Wait();
        try
        {
            _waitForClosedSource.TrySetException(exception);
        }
        finally
        {
            _sem.Release(1);
        }
    }

    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer) => InnerSocket.SendAsync(buffer);

    public ValueTask<int> ReceiveAsync(Memory<byte> buffer) => InnerSocket.ReceiveAsync(buffer);

    public async ValueTask DisposeAsync()
    {
        // guard with semaphore in case SignalDisconnected races
        await _sem.WaitAsync().ConfigureAwait(false);
        try
        {
            // dispose first, then signal
            try
            {
                await InnerSocket.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _waitForClosedSource.TrySetResult();
            }
        }
        finally
        {
            _sem.Release(1);
        }
    }
}
