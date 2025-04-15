using System.Net.WebSockets;
using System.Runtime.CompilerServices;
#if NETSTANDARD
using System.Runtime.InteropServices;
#endif

namespace NATS.Client.Core.Internal;

internal sealed class WebSocketConnection(NatsOpts natsOpts) : INatsSocketConnection
{
    private readonly ClientWebSocket _socket = new();
    private int _disposed;

    public async ValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            await natsOpts.WebSocketOpts.ApplyClientWebSocketOptionsAsync(_socket.Options, uri, natsOpts.TlsOpts, cancellationToken).ConfigureAwait(false);
            await _socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
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

        await _socket.SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
#else
        await _socket.SendAsync(buffer, WebSocketMessageType.Binary, WebSocketMessageFlags.EndOfMessage, CancellationToken.None).ConfigureAwait(false);
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

        var wsRead = await _socket.ReceiveAsync(segment, CancellationToken.None).ConfigureAwait(false);
#else
        var wsRead = await _socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
#endif
        return wsRead.Count;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) == 1)
        {
            try
            {
                using var cts = new CancellationTokenSource(natsOpts.ConnectTimeout);
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }

            _socket.Dispose();
        }
    }
}
