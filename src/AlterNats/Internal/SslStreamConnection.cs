using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace AlterNats.Internal;

internal sealed class SslStreamConnection : ISocketConnection
{
    readonly SslStream sslStream;
    readonly TaskCompletionSource<Exception> waitForClosedSource;
    readonly TlsOptions tlsOptions;
    readonly TlsCerts? tlsCerts;
    readonly CancellationTokenSource closeCts = new();
    int disposed;

    public SslStreamConnection(SslStream sslStream, TlsOptions tlsOptions, TlsCerts? tlsCerts, TaskCompletionSource<Exception> waitForClosedSource)
    {
        this.sslStream = sslStream;
        this.tlsOptions = tlsOptions;
        this.tlsCerts = tlsCerts;
        this.waitForClosedSource = waitForClosedSource;
    }

    public Task<Exception> WaitForClosed => waitForClosedSource.Task;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref disposed) == 1)
        {
            try
            {
                closeCts.Cancel();
                waitForClosedSource.TrySetCanceled();
            }
            catch
            {
            }

            return sslStream.DisposeAsync();
        }

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer)
    {
        await sslStream.WriteAsync(buffer, closeCts.Token).ConfigureAwait(false);
        return buffer.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer)
    {
        return sslStream.ReadAsync(buffer, closeCts.Token);
    }

    public async ValueTask AbortConnectionAsync(CancellationToken cancellationToken)
    {
        // SslStream.ShutdownAsync() doesn't accept a cancellation token, so check at the beginning of this method
        cancellationToken.ThrowIfCancellationRequested();
        await sslStream.ShutdownAsync().ConfigureAwait(false);
    }

    // when catch SocketClosedException, call this method.
    public void SignalDisconnected(Exception exception)
    {
        waitForClosedSource.TrySetResult(exception);
    }

    static X509Certificate LcsCbClientCerts(
        object sender,
        string targetHost,
        X509CertificateCollection localCertificates,
        X509Certificate? remoteCertificate,
        string[] acceptableIssuers) => localCertificates[0];

    static bool RcsCbInsecureSkipVerify(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors) => true;

    bool RcsCbCaCertChain(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // validate >=1 ca certs
        if (tlsCerts?.CaCerts == null || !tlsCerts.CaCerts.Any())
        {
            return false;
        }

        if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) != 0
            && chain != default
            && certificate != default)
        {
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.ExtraStore.AddRange(tlsCerts.CaCerts);
            if (chain.Build((X509Certificate2)certificate))
            {
                sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateChainErrors;
            }
        }

        // validate >= 1 chain elements and that last chain element was one of the supplied CA certs
        if (chain == default
            || !chain.ChainElements.Any()
            || !tlsCerts.CaCerts.Any(c => c.RawData.SequenceEqual(chain.ChainElements.Last().Certificate.RawData)))
        {
            sslPolicyErrors |= SslPolicyErrors.RemoteCertificateChainErrors;
        }

        return sslPolicyErrors == SslPolicyErrors.None;
    }

    SslClientAuthenticationOptions SslClientAuthenticationOptions(string targetHost)
    {
        if (tlsOptions.Disabled)
        {
            throw new InvalidOperationException("TLS is not permitted when TlsOptions.Disabled is set");
        }

        LocalCertificateSelectionCallback? lcsCb = default;
        if (tlsCerts?.ClientCerts != default && tlsCerts.ClientCerts.Any())
        {
            lcsCb = LcsCbClientCerts;
        }

        RemoteCertificateValidationCallback? rcsCb = default;
        if (tlsOptions.InsecureSkipVerify)
        {
            rcsCb = RcsCbInsecureSkipVerify;
        }
        else if (tlsCerts?.CaCerts != default && tlsCerts.CaCerts.Any())
        {
            rcsCb = RcsCbCaCertChain;
        }

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = SslProtocols.Tls12,
            ClientCertificates = tlsCerts?.ClientCerts,
            LocalCertificateSelectionCallback = lcsCb,
            RemoteCertificateValidationCallback = rcsCb
        };

        return options;
    }

    public async Task AuthenticateAsClientAsync(string target)
    {
        var options = SslClientAuthenticationOptions(target);
        await sslStream.AuthenticateAsClientAsync(options).ConfigureAwait(false);
    }
}
