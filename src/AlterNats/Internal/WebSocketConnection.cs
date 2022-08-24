using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;

namespace AlterNats.Internal;

internal sealed class WebSocketConnection : ISocketConnection
{
    readonly ClientWebSocket socket;
    readonly TaskCompletionSource<Exception> waitForClosedSource = new();
    int disposed;

    public Task<Exception> WaitForClosed => waitForClosedSource.Task;

    public WebSocketConnection()
    {
        this.socket = new ClientWebSocket();
    }

    // CancellationToken is not used, operation lifetime is completely same as socket.

    // socket is closed:
    //  receiving task returns 0 read
    //  throws SocketException when call method
    // socket is disposed:
    //  throws DisposedException

    // return ValueTask directly for performance, not care exception and signal-disconected.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        return socket.ConnectAsync(uri, cancellationToken);
    }

    /// <summary>
    /// Connect with Timeout. When failed, Dispose this connection.
    /// </summary>
    public async ValueTask ConnectAsync(Uri uri, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await socket.ConnectAsync(uri, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await DisposeAsync().ConfigureAwait(false);
            if (ex is OperationCanceledException)
            {
                throw new SocketException(10060); // 10060 = connection timeout.
            }
            else
            {
                throw;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer)
    {
        await socket.SendAsync(buffer, WebSocketMessageType.Binary, WebSocketMessageFlags.EndOfMessage, CancellationToken.None).ConfigureAwait(false);
        return buffer.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer)
    {
        var wsRead = await socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
        return wsRead.Count;
    }

    public ValueTask AbortConnectionAsync(CancellationToken cancellationToken)
    {
        socket.Abort();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref disposed) == 1)
        {
            try
            {
                waitForClosedSource.TrySetCanceled();
            }
            catch { }
            socket.Dispose();
        }
        return default;
    }

    // when catch SocketClosedException, call this method.
    public void SignalDisconnected(Exception exception)
    {
        waitForClosedSource.TrySetResult(exception);
    }
}
