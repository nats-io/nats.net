using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;

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
#if NET6_0
                _closeCts.Cancel();
#else
                await _closeCts.CancelAsync().ConfigureAwait(false);
#endif
                _waitForClosedSource.TrySetCanceled();
            }
            catch
            {
            }

            await _sslStream.DisposeAsync().ConfigureAwait(false);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer)
    {
        await _sslStream.WriteAsync(buffer, _closeCts.Token).ConfigureAwait(false);
        return buffer.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer)
    {
        return _sslStream.ReadAsync(buffer, _closeCts.Token);
    }

    public async ValueTask AbortConnectionAsync(CancellationToken cancellationToken)
    {
        // SslStream.ShutdownAsync() doesn't accept a cancellation token, so check at the beginning of this method
        cancellationToken.ThrowIfCancellationRequested();
        await _sslStream.ShutdownAsync().ConfigureAwait(false);
    }

    // when catch SocketClosedException, call this method.
    public void SignalDisconnected(Exception exception)
    {
        _waitForClosedSource.TrySetResult(exception);
    }

    public async Task AuthenticateAsClientAsync(NatsUri uri, TimeSpan timeout)
    {
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
    }
}
