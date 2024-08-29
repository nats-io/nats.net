using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

#if NETSTANDARD2_0
using System.Runtime.InteropServices;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
#endif

namespace NATS.Client.Core.Internal;

internal sealed class SslStreamConnection : ISocketConnection
{
    private readonly ILogger _logger;
    private readonly Socket _socket;
    private readonly TaskCompletionSource<Exception> _waitForClosedSource;
    private readonly NatsTlsOpts _tlsOpts;
    private readonly CancellationTokenSource _closeCts = new();
    private int _disposed;
    private SslStream? _sslStream;

    public SslStreamConnection(ILogger logger, Socket socket, NatsTlsOpts tlsOpts, TaskCompletionSource<Exception> waitForClosedSource)
    {
        _logger = logger;
        _socket = socket;
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
                // ignored
            }

            if (_sslStream != null)
            {
#if NETSTANDARD2_0
                _sslStream.Dispose();
#else
                await _sslStream.DisposeAsync().ConfigureAwait(false);
#endif
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer)
    {
#if NETSTANDARD2_0
        if (MemoryMarshal.TryGetArray(buffer, out var segment) == false)
        {
            segment = new ArraySegment<byte>(buffer.ToArray());
        }

        await _sslStream!.WriteAsync(segment.Array, segment.Offset, segment.Count, _closeCts.Token).ConfigureAwait(false);
#else
        await _sslStream!.WriteAsync(buffer, _closeCts.Token).ConfigureAwait(false);
#endif
        return buffer.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer)
    {
#if NETSTANDARD2_0
        if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment) == false)
        {
            ThrowHelper.ThrowInvalidOperationException("Can't get underlying array");
        }

        return new ValueTask<int>(_sslStream!.ReadAsync(segment.Array!, segment.Offset, segment.Count, _closeCts.Token));
#else
        return _sslStream!.ReadAsync(buffer, _closeCts.Token);
#endif
    }

    public async ValueTask AbortConnectionAsync(CancellationToken cancellationToken)
    {
        // SslStream.ShutdownAsync() doesn't accept a cancellation token, so check at the beginning of this method
        cancellationToken.ThrowIfCancellationRequested();
        if (_sslStream != null)
        {
#if NETSTANDARD2_0
            _sslStream.Close();
#else
            await _sslStream.ShutdownAsync().ConfigureAwait(false);
#endif
        }
    }

    // when catch SocketClosedException, call this method.
    public void SignalDisconnected(Exception exception)
    {
        _waitForClosedSource.TrySetResult(exception);
    }

    public async Task AuthenticateAsClientAsync(NatsUri uri, TimeSpan timeout)
    {
        var options = await _tlsOpts.AuthenticateAsClientOptionsAsync(uri).ConfigureAwait(true);
        using var networkStream = new NetworkStream(_socket, true);

        _sslStream?.Dispose();

#if NETSTANDARD2_0
        _sslStream = new SslStream(
            innerStream: networkStream,
            leaveInnerStreamOpen: false,
            userCertificateSelectionCallback: options.LocalCertificateSelectionCallback,
            userCertificateValidationCallback: options.RemoteCertificateValidationCallback);
        try
        {
            await _sslStream.AuthenticateAsClientAsync(
                targetHost: options.TargetHost,
                clientCertificates: options.ClientCertificates,
                enabledSslProtocols: options.EnabledSslProtocols,
                checkCertificateRevocation: options.CertificateRevocationCheckMode != X509RevocationMode.NoCheck).ConfigureAwait(false);
        }
        catch (AuthenticationException ex)
        {
            throw new NatsException($"TLS authentication failed", ex);
        }
#else
        _sslStream = new SslStream(innerStream: networkStream);
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
