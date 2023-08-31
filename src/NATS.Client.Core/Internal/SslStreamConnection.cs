using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace NATS.Client.Core.Internal;

internal sealed class SslStreamConnection : ISocketConnection
{
    private readonly SslStream _sslStream;
    private readonly TaskCompletionSource<Exception> _waitForClosedSource;
    private readonly NatsTlsOpts _tlsOpts;
    private readonly TlsCerts? _tlsCerts;
    private readonly CancellationTokenSource _closeCts = new();
    private int _disposed;

    public SslStreamConnection(SslStream sslStream, NatsTlsOpts tlsOpts, TlsCerts? tlsCerts, TaskCompletionSource<Exception> waitForClosedSource)
    {
        _sslStream = sslStream;
        _tlsOpts = tlsOpts;
        _tlsCerts = tlsCerts;
        _waitForClosedSource = waitForClosedSource;
    }

    public Task<Exception> WaitForClosed => _waitForClosedSource.Task;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) == 1)
        {
            try
            {
                _closeCts.Cancel();
                _waitForClosedSource.TrySetCanceled();
            }
            catch
            {
            }

            return _sslStream.DisposeAsync();
        }

        return default;
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

    public async Task AuthenticateAsClientAsync(string target)
    {
        var options = SslClientAuthenticationOptions(target);
        await _sslStream.AuthenticateAsClientAsync(options).ConfigureAwait(false);
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

        return sslPolicyErrors == SslPolicyErrors.None;
    }

    private SslClientAuthenticationOptions SslClientAuthenticationOptions(string targetHost)
    {
        if (_tlsOpts.Disabled)
        {
            throw new InvalidOperationException("TLS is not permitted when TlsOptions.Disabled is set");
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

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = SslProtocols.Tls12,
            ClientCertificates = _tlsCerts?.ClientCerts,
            LocalCertificateSelectionCallback = lcsCb,
            RemoteCertificateValidationCallback = rcsCb,
        };

        return options;
    }
}
