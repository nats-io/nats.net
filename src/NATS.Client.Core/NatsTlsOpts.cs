using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

/// <summary>
/// TLS mode to use during connection.
/// </summary>
public enum TlsMode
{
    /// <summary>
    /// For connections that use the "nats://" scheme and don't supply Client or CA Certificates - same as <c>Prefer</c>
    /// For connections that use the "tls://" scheme or supply Client or CA Certificates - same as <c>Require</c>
    /// </summary>
    Auto,

    /// <summary>
    /// if the Server supports TLS, then use it, otherwise use plain-text.
    /// </summary>
    Prefer,

    /// <summary>
    /// Forces the connection to upgrade to TLS. if the Server does not support TLS, then fail the connection.
    /// </summary>
    Require,

    /// <summary>
    /// Upgrades the connection to TLS as soon as the connection is established.
    /// </summary>
    Implicit,

    /// <summary>
    /// Disabled mode will not attempt to upgrade the connection to TLS.
    /// </summary>
    Disable,
}

/// <summary>
/// Immutable options for TlsOptions, you can configure via `with` operator.
/// These options are ignored in WebSocket connections
/// </summary>
public sealed record NatsTlsOpts
{
    public static readonly NatsTlsOpts Default = new();

    /// <summary>
    /// String or file path to PEM-encoded X509 Certificate
    /// </summary>
    /// <remarks>
    /// Must be used in conjunction with <see cref="KeyFile"/>.
    /// </remarks>
    public string? CertFile { get; init; }

    /// <summary>
    /// String or file path to PEM-encoded Private Key
    /// </summary>
    /// <remarks>
    /// Must be used in conjunction with <see cref="CertFile"/>.
    /// </remarks>
    public string? KeyFile { get; init; }

    /// <summary>
    /// Callback that loads Client Certificate
    /// </summary>
    /// <remarks>
    /// If the Client Certificate includes intermediates, use
    /// LoadClientCertContext (.NET 8 and above) instead.
    /// </remarks>
    public Func<ValueTask<X509Certificate2>>? LoadClientCert { get; init; }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Callback that loads Client Certificate
    /// </summary>
    /// <remarks>This option is only available in .NET 8 and above.</remarks>
    public Func<ValueTask<SslStreamCertificateContext>>? LoadClientCertContext { get; init; }
#endif

    /// <summary>
    /// String or file path to PEM-encoded X509 CA Certificate
    /// </summary>
    public string? CaFile { get; init; }

    /// <summary>
    /// Callback that loads CA Certificates
    /// </summary>
    public Func<ValueTask<X509Certificate2Collection>>? LoadCaCerts { get; init; }

    /// <summary>When true, skip remote certificate verification and accept any server certificate</summary>
    public bool InsecureSkipVerify { get; init; }

    /// <summary>Certificate revocation mode for certificate validation.</summary>
    /// <value>One of the values in <see cref="T:System.Security.Cryptography.X509Certificates.X509RevocationMode" />. The default is <see langword="NoCheck" />.</value>
    public X509RevocationMode CertificateRevocationCheckMode { get; init; }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Gets or sets an optional customized policy for remote certificate validation.
    /// If not <see langword="null"/>,
    /// <see cref="CertificateRevocationCheckMode"/> and
    /// <see cref="T:System.Net.Security.SslCertificateTrust" /> are ignored.
    /// </summary>
    /// <remarks>This option is only available in .NET 8 and above.</remarks>
    public X509ChainPolicy? CertificateChainPolicy { get; init; }
#endif

    /// <summary>TLS mode to use during connection</summary>
    public TlsMode Mode { get; init; }

    internal bool HasTlsCerts => CertFile != default || KeyFile != default || LoadClientCert != default || CaFile != default || LoadCaCerts != default;

    /// <summary>
    /// Helper method to load a Client Certificate from a pem-encoded string
    /// </summary>
    /// <remarks>
    /// If the Client Certificate includes intermediates, use
    /// LoadClientCertContextFromPem (.NET 8 and above) instead.
    /// </remarks>
    public static Func<ValueTask<X509Certificate2>> LoadClientCertFromPem(string certPem, string keyPem)
    {
        var clientCert = X509Certificate2.CreateFromPem(certPem, keyPem);
        return () => ValueTask.FromResult(clientCert);
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Helper method to load a Client Certificate Context from a pem-encoded string
    /// </summary>
    /// <param name="certPem">string with the PEM-encoded X509 certificate</param>
    /// <param name="keyPem">string with the PEM-encoded private key</param>
    /// <param name="offline">
    /// <see langword="false" /> to indicate that the missing certificates can be downloaded from the network;
    /// <see langword="true" /> to indicate that only available X509Certificate stores should be searched for missing certificates.
    /// </param>
    /// <param name="trust">An optional trust policy, to replace the default system trust.</param>
    public static Func<ValueTask<SslStreamCertificateContext>> LoadClientCertContextFromPem(string certPem, string keyPem, bool offline = false, SslCertificateTrust? trust = null)
    {
        var leafCert = X509Certificate2.CreateFromPem(certPem, keyPem);
        var intermediateCerts = new X509Certificate2Collection();
        intermediateCerts.ImportFromPem(certPem);
        if (intermediateCerts.Count > 0)
        {
            intermediateCerts.RemoveAt(0);
        }

        var clientCertContext = SslStreamCertificateContext.Create(leafCert, intermediateCerts, offline, trust);
        return () => ValueTask.FromResult(clientCertContext);
    }
#endif

    /// <summary>
    /// Helper method to load CA Certificates from a pem-encoded string
    /// </summary>
    public static Func<ValueTask<X509Certificate2Collection>> LoadCaCertsFromPem(string caPem)
    {
        var caCerts = new X509Certificate2Collection();
        caCerts.ImportFromPem(caPem);
        return () => ValueTask.FromResult(caCerts);
    }

    internal TlsMode EffectiveMode(NatsUri uri) => Mode switch
    {
        TlsMode.Auto => HasTlsCerts || uri.Uri.Scheme.ToLower() == "tls" ? TlsMode.Require : TlsMode.Prefer,
        _ => Mode,
    };

    internal bool TryTls(NatsUri uri)
    {
        var effectiveMode = EffectiveMode(uri);
        return effectiveMode is TlsMode.Require or TlsMode.Prefer;
    }
}
