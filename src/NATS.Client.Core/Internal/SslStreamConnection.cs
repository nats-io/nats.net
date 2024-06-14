using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;

#if NETSTANDARD2_0
using System.Runtime.InteropServices;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
#endif

namespace NATS.Client.Core.Internal;

internal sealed class SslStreamConnection : ISocketConnection
{
    private readonly ILogger _logger;
    private readonly SslStream _sslStream;
    private readonly TaskCompletionSource<Exception> _waitForClosedSource;
    private readonly NatsTlsOpts _tlsOpts;
    private readonly CancellationTokenSource _closeCts = new();
    private int _disposed;

    public SslStreamConnection(ILogger logger, SslStream sslStream, NatsTlsOpts tlsOpts, TaskCompletionSource<Exception> waitForClosedSource)
    {
        _logger = logger;
        _sslStream = sslStream;
        _tlsOpts = tlsOpts;
        _waitForClosedSource = waitForClosedSource;
    }

    public Task<Exception> WaitForClosed => _waitForClosedSource.Task;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) == 1)
        {
            try
            {
#if NET8_0_OR_GREATER
                await _closeCts.CancelAsync().ConfigureAwait(false);
#else
                _closeCts.Cancel();
#endif
                _waitForClosedSource.TrySetCanceled();
            }
            catch
            {
            }

#if NETSTANDARD2_0
            _sslStream.Dispose();
#else
            await _sslStream.DisposeAsync().ConfigureAwait(false);
#endif
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer)
    {
#if NETSTANDARD2_0
        MemoryMarshal.TryGetArray(buffer, out var segment);
        await _sslStream.WriteAsync(segment.Array, segment.Offset, segment.Count, _closeCts.Token).ConfigureAwait(false);
#else
        await _sslStream.WriteAsync(buffer, _closeCts.Token).ConfigureAwait(false);
#endif
        return buffer.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer)
    {
#if NETSTANDARD2_0
        MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment);
        return new ValueTask<int>(_sslStream.ReadAsync(segment.Array!, segment.Offset, segment.Count, _closeCts.Token));
#else
        return _sslStream.ReadAsync(buffer, _closeCts.Token);
#endif
    }

    public async ValueTask AbortConnectionAsync(CancellationToken cancellationToken)
    {
        // SslStream.ShutdownAsync() doesn't accept a cancellation token, so check at the beginning of this method
        cancellationToken.ThrowIfCancellationRequested();
#if NETSTANDARD2_0
        _sslStream.Close();
#else
        await _sslStream.ShutdownAsync().ConfigureAwait(false);
#endif
    }

    // when catch SocketClosedException, call this method.
    public void SignalDisconnected(Exception exception)
    {
        _waitForClosedSource.TrySetResult(exception);
    }

    public async Task AuthenticateAsClientAsync(NatsUri uri, TimeSpan timeout)
    {
#if NETSTANDARD
        try
        {
            await _sslStream.AuthenticateAsClientAsync(uri.Host).ConfigureAwait(false);
        }
        catch (AuthenticationException ex)
        {
            throw new NatsException($"TLS authentication failed", ex);
        }
#else
        var options = await _tlsOpts.AuthenticateAsClientOptionsAsync(uri).ConfigureAwait(true);
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await _sslStream.AuthenticateAsClientAsync(options, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new NatsException("TLS authentication timed out");
        }
        catch (AuthenticationException ex)
        {
            throw new NatsException($"TLS authentication failed", ex);
        }
#endif
    }
}
