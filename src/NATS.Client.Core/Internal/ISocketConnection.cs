namespace NATS.Client.Core.Internal;

internal interface ISocketConnection : IAsyncDisposable
{
    public Task<Exception> WaitForClosed { get; }

    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer);

    public ValueTask<int> ReceiveAsync(Memory<byte> buffer);

    public ValueTask AbortConnectionAsync(CancellationToken cancellationToken);

    public void SignalDisconnected(Exception exception);
}
