using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
#if NETSTANDARD2_0
using System.Runtime.InteropServices;
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
#if NETSTANDARD
        return new ValueTask(_socket.ConnectAsync(host, port).WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken));
#else
        return _socket.ConnectAsync(host, port, cancellationToken);
#endif
    }

    /// <summary>
    /// Connect with Timeout. When failed, Dispose this connection.
    /// </summary>
    public async ValueTask ConnectAsync(string host, int port, NatsOpts opts)
    {
        using var cts = new CancellationTokenSource(opts.ConnectTimeout);
        try
        {
            var proxyOpts = opts.ProxyOpts;
            if (proxyOpts.Host != null)
            {
#if NETSTANDARD
                await _socket.ConnectAsync(proxyOpts.Host, proxyOpts.Port).WaitAsync(opts.ConnectTimeout, cts.Token).ConfigureAwait(false);
#else
                await _socket.ConnectAsync(proxyOpts.Host, proxyOpts.Port, cts.Token).ConfigureAwait(false);
#endif

                string? connectAuth = null;
                if (proxyOpts.Auth != null)
                {
                    // Create the CONNECT request with proxy authentication
                    var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(proxyOpts.Auth));
                    connectAuth = $"Proxy-Authorization: Basic {auth}\r\n";
                }

                var serverWithPort = $"{host}:{port}";
                var connectBuffer = Encoding.UTF8.GetBytes($"CONNECT {serverWithPort} HTTP/1.1\r\nHost: {serverWithPort}\r\n{connectAuth}Proxy-Connection: Keep-Alive\r\n\r\n");

                // Send CONNECT request to proxy
                await SendAsync(connectBuffer).ConfigureAwait(false);

                // Validate proxy response
                var receiveBuffer = new byte[4096];
                var responseBuilder = new StringBuilder();
                int read;
                do
                {
                    read = await ReceiveAsync(receiveBuffer).ConfigureAwait(false);
                    responseBuilder.Append(Encoding.UTF8.GetString(receiveBuffer, 0, read));
                } while (read > 0 && !responseBuilder.ToString().Contains("\r\n\r\n"));

                var response = responseBuilder.ToString();
                if (!response.Contains("200 Connection established"))
                    throw new Exception($"Proxy connection failed. Response: {response}");
            }
            else
            {
#if NETSTANDARD
                await _socket.ConnectAsync(host, port).WaitAsync(opts.ConnectTimeout, cts.Token).ConfigureAwait(false);
#else
                await _socket.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
#endif
            }
        }
        catch (Exception ex)
        {
            await DisposeAsync().ConfigureAwait(false);
            if (ex is OperationCanceledException)
            {
                throw new SocketException(10060); // 10060 = connection timeout.
            }

            throw;
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

        return new ValueTask<int>(_socket.SendAsync(segment, SocketFlags.None));
#else
        return _socket.SendAsync(buffer, SocketFlags.None, CancellationToken.None);
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

        return new ValueTask<int>(_socket.ReceiveAsync(segment, SocketFlags.None));
#else
        return _socket.ReceiveAsync(buffer, SocketFlags.None, CancellationToken.None);
#endif
    }

    public ValueTask AbortConnectionAsync(CancellationToken cancellationToken)
    {
#if NETSTANDARD
        _socket.Disconnect(false);
        return default;
#else
        return _socket.DisconnectAsync(false, cancellationToken);
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
                // ignored
            }

            try
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                // ignored
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
                _socket,
                tlsOpts,
                _waitForClosedSource);
        }

        throw new ObjectDisposedException(nameof(TcpConnection));
    }
}
