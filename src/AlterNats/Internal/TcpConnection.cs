using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace AlterNats.Internal;

internal sealed class SocketClosedException : Exception
{
    public SocketClosedException(Exception? innerException)
        : base("Socket has been closed.", innerException)
    {
    }
}

internal sealed class TcpConnection : ISocketConnection
{
    readonly Socket socket;
    readonly TaskCompletionSource<Exception> waitForClosedSource = new();
    int disposed;

    public Task<Exception> WaitForClosed => waitForClosedSource.Task;

    public TcpConnection()
    {
        this.socket = new Socket(Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (Socket.OSSupportsIPv6)
        {
            socket.DualMode = true;
        }

        socket.NoDelay = true;
        socket.SendBufferSize = 0;
        socket.ReceiveBufferSize = 0;
    }

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
        return socket.ConnectAsync(host, port, cancellationToken);
    }

    /// <summary>
    /// Connect with Timeout. When failed, Dispose this connection.
    /// </summary>
    public async ValueTask ConnectAsync(string host, int port, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await socket.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
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
        return socket.SendAsync(buffer, SocketFlags.None, CancellationToken.None);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer)
    {
        return socket.ReceiveAsync(buffer, SocketFlags.None, CancellationToken.None);
    }

    public ValueTask AbortConnectionAsync(CancellationToken cancellationToken)
    {
        return socket.DisconnectAsync(false, cancellationToken);
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

    // NetworkStream will own the Socket, so mark as disposed
    // in order to skip socket.Dispose() in DisposeAsync
    public SslStreamConnection UpgradeToSslStreamConnection(TlsOptions tlsOptions, TlsCerts? tlsCerts)
    {
        if (Interlocked.Increment(ref disposed) == 1)
        {
            return new SslStreamConnection(
                new SslStream(new NetworkStream(socket, true)),
                tlsOptions,
                tlsCerts,
                waitForClosedSource);
        }

        throw new ObjectDisposedException(nameof(TcpConnection));
    }
}
