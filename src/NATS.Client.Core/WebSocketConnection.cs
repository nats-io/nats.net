using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
#if NETSTANDARD
using System.Runtime.InteropServices;
#endif

namespace NATS.Client.Core;

public class WebSocketConnection : ISocketConnection
{
    private readonly TaskCompletionSource<Exception> _waitForClosedSource = new();
    private readonly TimeSpan _socketCloseTimeout = TimeSpan.FromSeconds(5); // matches _socketComponentDisposeTimeout in NatsConnection.cs
    private int _disposed;

    public ClientWebSocket Socket { get; } = new();

    public Task<Exception> WaitForClosed => _waitForClosedSource.Task;

    // CancellationToken is not used, operation lifetime is completely same as socket.

    // socket is closed:
    //  receiving task returns 0 read
    //  throws SocketException when call method
    // socket is disposed:
    //  throws DisposedException

    // return ValueTask directly for performance, not care exception and signal-disconnected.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        return Socket.ConnectAsync(uri, cancellationToken);
    }

    /// <summary>
    /// Connect with Timeout. When failed, Dispose this connection.
    /// </summary>
    public async ValueTask ConnectAsync(NatsUri uri, NatsOpts opts)
    {
        using var cts = new CancellationTokenSource(opts.ConnectTimeout);
        try
        {
            await opts.WebSocketOpts.ApplyClientWebSocketOptionsAsync(Socket.Options, uri, opts.TlsOpts, cts.Token).ConfigureAwait(false);
            await Socket.ConnectAsync(uri.Uri, cts.Token).ConfigureAwait(false);
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
#if NETSTANDARD
        if (MemoryMarshal.TryGetArray(buffer, out var segment) == false)
        {
            segment = new ArraySegment<byte>(buffer.ToArray());
        }

        await Socket.SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
#else
        await Socket.SendAsync(buffer, WebSocketMessageType.Binary, WebSocketMessageFlags.EndOfMessage, CancellationToken.None).ConfigureAwait(false);
#endif
        return buffer.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer)
    {
#if NETSTANDARD2_0
        if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment) == false)
        {
            ThrowHelper.ThrowInvalidOperationException("Can't get underlying array");
        }

        var wsRead = await Socket.ReceiveAsync(segment, CancellationToken.None).ConfigureAwait(false);
#else
        var wsRead = await Socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
#endif
        return wsRead.Count;
    }

    public ValueTask AbortConnectionAsync(CancellationToken cancellationToken)
    {
        // ClientWebSocket.Abort() doesn't accept a cancellation token, so check at the beginning of this method
        cancellationToken.ThrowIfCancellationRequested();
        Socket.Abort();
        return default;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) == 1)
        {
            try
            {
                _waitForClosedSource.TrySetCanceled();
            }
            catch
            {
            }

            try
            {
                var cts = new CancellationTokenSource(_socketCloseTimeout);
                await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, default, cts.Token).ConfigureAwait(false);
            }
            catch
            {
            }

            Socket.Dispose();
        }
    }

    // when catch SocketClosedException, call this method.
    public void SignalDisconnected(Exception exception)
    {
        _waitForClosedSource.TrySetResult(exception);
    }
}
