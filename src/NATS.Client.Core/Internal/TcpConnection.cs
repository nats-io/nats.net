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
    private readonly NatsOpts _natsOpts;
    private int _disposed;

    public TcpConnection(NatsOpts natsOpts)
    {
        _natsOpts = natsOpts;
        Socket = new Socket(Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (Socket.OSSupportsIPv6)
        {
            Socket.DualMode = true;
        }

        Socket.NoDelay = true;
    }

    public Socket Socket { get; }

    public async ValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
#if NETSTANDARD
            await Socket.ConnectAsync(uri.Host, uri.Port).WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
#else
            await Socket.ConnectAsync(uri.Host, uri.Port, cancellationToken).ConfigureAwait(false);
#endif
        }
        catch (Exception)
        {
            await DisposeAsync().ConfigureAwait(false);
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
                Socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                // ignored
            }
            finally
            {
                Socket.Dispose();
            }
        }

        return default;
    }
}
