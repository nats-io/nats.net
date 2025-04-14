using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NATS.Client.Core.Internal;

internal sealed class SocketClosedException : Exception
{
    public SocketClosedException(Exception? innerException)
        : base("Socket has been closed.", innerException)
    {
    }
}

internal sealed class TcpConnection : INatsTlsUpgradeableSocketConnection
{
    private readonly TaskCompletionSource<Exception> _waitForClosedSource = new();
    private int _disposed;

    public TcpConnection()
    {
        Socket = new Socket(Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (Socket.OSSupportsIPv6)
        {
            Socket.DualMode = true;
        }

        Socket.NoDelay = true;
    }

    public Socket Socket { get; }

    public Task<Exception> WaitForClosed => _waitForClosedSource.Task;

    // CancellationToken is not used, operation lifetime is completely same as socket.

    // socket is closed:
    //  receiving task returns 0 read
    //  throws SocketException when call method
    // socket is disposed:
    //  throws DisposedException

    /// <summary>
    /// Connect with Timeout. When failed, Dispose this connection.
    /// </summary>
    public async ValueTask ConnectAsync(Uri uri, NatsOpts opts, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(opts.ConnectTimeout);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
#if NETSTANDARD
            await Socket.ConnectAsync(uri.Host, uri.Port).WaitAsync(Timeout.InfiniteTimeSpan, cts.Token).ConfigureAwait(false);
#else
            await Socket.ConnectAsync(uri.Host, uri.Port, cts.Token).ConfigureAwait(false);
#endif
        }
        catch (Exception ex)
        {
            await DisposeAsync().ConfigureAwait(false);
            if (ex is OperationCanceledException && timeoutCts.Token.IsCancellationRequested)
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
    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer)
    {
#if NETSTANDARD2_0
        if (MemoryMarshal.TryGetArray(buffer, out var segment) == false)
        {
            segment = new ArraySegment<byte>(buffer.ToArray());
        }

        return new ValueTask<int>(Socket.SendAsync(segment, SocketFlags.None));
#else
        return Socket.SendAsync(buffer, SocketFlags.None, CancellationToken.None);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer)
    {
#if NETSTANDARD2_0
        if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment) == false)
        {
            ThrowHelper.ThrowInvalidOperationException("Can't get underlying array");
        }

        return new ValueTask<int>(Socket.ReceiveAsync(segment, SocketFlags.None));
#else
        return Socket.ReceiveAsync(buffer, SocketFlags.None, CancellationToken.None);
#endif
    }

    public ValueTask AbortConnectionAsync(CancellationToken cancellationToken)
    {
#if NETSTANDARD
        Socket.Disconnect(false);
        return default;
#else
        return Socket.DisconnectAsync(false, cancellationToken);
#endif
    }

    public ValueTask DisposeAsync()
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
                Socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }

            Socket.Dispose();
        }

        return default;
    }

    // when catch SocketClosedException, call this method.
    public void SignalDisconnected(Exception exception)
    {
        _waitForClosedSource.TrySetResult(exception);
    }
}
