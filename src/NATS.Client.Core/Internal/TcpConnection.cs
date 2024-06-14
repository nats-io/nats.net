using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
#if NETSTANDARD
using NATS.Client.Core.Internal.NetStandardExtensions;
#endif

namespace NATS.Client.Core.Internal;

internal sealed class SocketClosedException : Exception
{
    public SocketClosedException(Exception? innerException)
        : base("Socket has been closed.", innerException)
    {
    }
}

internal sealed class TcpConnection : ISocketConnection
{
    private readonly ILogger _logger;
    private readonly Socket _socket;
    private readonly TaskCompletionSource<Exception> _waitForClosedSource = new();
    private int _disposed;

    public TcpConnection(ILogger logger)
    {
        _logger = logger;
        _socket = new Socket(Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (Socket.OSSupportsIPv6)
        {
            _socket.DualMode = true;
        }

        _socket.NoDelay = true;
    }

    public Task<Exception> WaitForClosed => _waitForClosedSource.Task;

    // CancellationToken is not used, operation lifetime is completely same as socket.

    // socket is closed:
    //  receiving task returns 0 read
    //  throws SocketException when call method
    // socket is disposed:
    //  throws DisposedException

    // return ValueTask directly for performance, not care exception and signal-disconnected.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
#if NET6_0_OR_GREATER
        return _socket.ConnectAsync(host, port, cancellationToken);
#else
        return new ValueTask(_socket.ConnectAsync(host, port).WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken));
#endif
    }

    /// <summary>
    /// Connect with Timeout. When failed, Dispose this connection.
    /// </summary>
    public async ValueTask ConnectAsync(string host, int port, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
#if NET6_0_OR_GREATER
            await _socket.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
#else
            await _socket.ConnectAsync(host, port).WaitAsync(timeout, cts.Token).ConfigureAwait(false);
#endif
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
    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer)
    {
#if NETSTANDARD2_0
        MemoryMarshal.TryGetArray(buffer, out var segment);
        return new ValueTask<int>(_socket.SendAsync(segment, SocketFlags.None));
#else
        return _socket.SendAsync(buffer, SocketFlags.None, CancellationToken.None);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer)
    {
#if NETSTANDARD2_0
        MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment);
        return new ValueTask<int>(_socket.ReceiveAsync(segment, SocketFlags.None));
#else
        return _socket.ReceiveAsync(buffer, SocketFlags.None, CancellationToken.None);
#endif
    }

    public ValueTask AbortConnectionAsync(CancellationToken cancellationToken)
    {
#if NET6_0_OR_GREATER
        return _socket.DisconnectAsync(false, cancellationToken);
#else
        _socket.Disconnect(false);
        return default;
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
                _socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }

            _socket.Dispose();
        }

        return default;
    }

    // when catch SocketClosedException, call this method.
    public void SignalDisconnected(Exception exception)
    {
        _waitForClosedSource.TrySetResult(exception);
    }

    // NetworkStream will own the Socket, so mark as disposed
    // in order to skip socket.Dispose() in DisposeAsync
    public SslStreamConnection UpgradeToSslStreamConnection(NatsTlsOpts tlsOpts)
    {
        if (Interlocked.Increment(ref _disposed) == 1)
        {
            return new SslStreamConnection(
                _logger,
                new SslStream(new NetworkStream(_socket, true)),
                tlsOpts,
                _waitForClosedSource);
        }

        throw new ObjectDisposedException(nameof(TcpConnection));
    }
}
