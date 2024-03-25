using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace NATS.Client.Core.Internal;

internal sealed class SslStreamConnection : ISocketConnection
{
    private readonly ILogger _logger;
    private readonly SslStream _sslStream;
    private readonly TaskCompletionSource<Exception> _waitForClosedSource;
    private readonly NatsTlsOpts _tlsOpts;
    private readonly TlsCerts? _tlsCerts;
    private readonly CancellationTokenSource _closeCts = new();
    private int _disposed;

    public SslStreamConnection(ILogger logger, SslStream sslStream, NatsTlsOpts tlsOpts, TlsCerts? tlsCerts, TaskCompletionSource<Exception> waitForClosedSource)
    {
        _logger = logger;
        _sslStream = sslStream;
        _tlsOpts = tlsOpts;
        _tlsCerts = tlsCerts;
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
        var options = SslClientAuthenticationOptions(uri);
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

    private static X509Certificate LcsCbClientCerts(
        object sender,
        string targetHost,
        X509CertificateCollection localCertificates,
        X509Certificate? remoteCertificate,
        string[] acceptableIssuers) => localCertificates[0];

    private static bool RcsCbInsecureSkipVerify(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors) => true;

    private bool RcsCbCaCertChain(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // validate >=1 ca certs
        if (_tlsCerts?.CaCerts == null || !_tlsCerts.CaCerts.Any())
        {
            return false;
        }

        if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) != 0
            && chain != default
            && certificate != default)
        {
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.ExtraStore.AddRange(_tlsCerts.CaCerts);
            if (chain.Build((X509Certificate2)certificate))
            {
                sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateChainErrors;
            }
        }

        // validate >= 1 chain elements and that last chain element was one of the supplied CA certs
        if (chain == default
            || !chain.ChainElements.Any()
            || !_tlsCerts.CaCerts.Any(c => c.RawData.SequenceEqual(chain.ChainElements.Last().Certificate.RawData)))
        {
            sslPolicyErrors |= SslPolicyErrors.RemoteCertificateChainErrors;
        }

        var success = sslPolicyErrors == SslPolicyErrors.None;

        if (!success)
        {
            _logger.LogError(NatsLogEvents.Security, "TLS certificate validation failed: {SslPolicyErrors}", sslPolicyErrors);
        }

        return success;
    }

    private SslClientAuthenticationOptions SslClientAuthenticationOptions(NatsUri uri)
    {
        if (_tlsOpts.EffectiveMode(uri) == TlsMode.Disable)
        {
            throw new InvalidOperationException("TLS is not permitted when TlsMode is set to Disable");
        }

        LocalCertificateSelectionCallback? lcsCb = default;
        if (_tlsCerts?.ClientCerts != default && _tlsCerts.ClientCerts.Any())
        {
            lcsCb = LcsCbClientCerts;
        }

        RemoteCertificateValidationCallback? rcsCb = default;
        if (_tlsOpts.InsecureSkipVerify)
        {
            rcsCb = RcsCbInsecureSkipVerify;
        }
        else if (_tlsCerts?.CaCerts != default && _tlsCerts.CaCerts.Any())
        {
            rcsCb = RcsCbCaCertChain;
        }

#if NET8_0_OR_GREATER
        X509ChainPolicy? policy = null;
        SslStreamCertificateContext? streamCertificateContext = null;
        if (_tlsCerts?.ClientCerts != null && _tlsCerts.CaCerts != null && _tlsCerts.ClientCerts.Count >= 1)
        {
            streamCertificateContext = SslStreamCertificateContext.Create(
                _tlsCerts.ClientCerts[0],
                _tlsCerts.ClientCerts,
                trust: SslCertificateTrust.CreateForX509Collection(_tlsCerts.CaCerts));

            policy = new()
            {
                RevocationMode = _tlsOpts.CertificateRevocationCheckMode,
                TrustMode = X509ChainTrustMode.CustomRootTrust,
            };

            policy.CustomTrustStore.AddRange(_tlsCerts.CaCerts);

            if (_tlsCerts.ClientCerts.Count > 1)
            {
                policy.ExtraStore.AddRange(_tlsCerts.ClientCerts);
            }
        }

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = uri.Host,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ClientCertificates = _tlsCerts?.ClientCerts,
            ClientCertificateContext = streamCertificateContext,
            CertificateChainPolicy = policy,
            LocalCertificateSelectionCallback = lcsCb,
            RemoteCertificateValidationCallback = rcsCb,
            CertificateRevocationCheckMode = _tlsOpts.CertificateRevocationCheckMode,
        };
#else
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = uri.Host,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ClientCertificates = _tlsCerts?.ClientCerts,
            LocalCertificateSelectionCallback = lcsCb,
            RemoteCertificateValidationCallback = rcsCb,
            CertificateRevocationCheckMode = _tlsOpts.CertificateRevocationCheckMode,
        };
#endif

        return options;
    }
}
