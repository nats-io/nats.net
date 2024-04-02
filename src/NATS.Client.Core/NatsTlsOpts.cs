using System.Net.Security;
using System.Security.Cryptography;
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
    /// /// <remarks>
    /// Must be used in conjunction with <see cref="CertFile"/>.
    /// </remarks>
    public string? KeyFile { get; init; }

    /// <summary>
    /// Callback that loads Client Certificate
    /// </summary>
    /// <remarks>
    /// Callback may return multiple certificates, in which case the first
    /// certificate is used as the client certificate and the rest are used as intermediates.
    /// Using intermediate certificates is only supported on targets .NET 8 and above.
    /// </remarks>
    public Func<ValueTask<X509Certificate2Collection>>? LoadClientCert { get; init; }

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

    /// <summary>
    /// Gets or sets an optional customized policy for remote certificate
    /// validation. If not <see langword="null"/>,
    /// <see cref="CertificateRevocationCheckMode"/> and <see cref="SslCertificateTrust"/>
    /// are ignored.
    /// </summary>
    /// <remarks>This option is only available in .NET 8 and above.</remarks>
    public X509ChainPolicy? CertificateChainPolicy { get; init; }

    /// <summary>TLS mode to use during connection</summary>
    public TlsMode Mode { get; init; }

    internal bool HasTlsCerts => CertFile != default || KeyFile != default || LoadClientCert != default || CaFile != default || LoadCaCerts != default;

    /// <summary>
    /// Helper method to load a client certificate and its key from PEM-encoded texts.
    /// </summary>
    /// <param name="certPem">Text of PEM-encoded certificates</param>
    /// <param name="keyPem">Text of PEM-encoded key</param>
    /// <returns>Returns a callback that will return a collection of certificates</returns>
    /// <remarks>
    /// Client certificate string may contain multiple certificates, in which case the first
    /// certificate is used as the client certificate and the rest are used as intermediates.
    /// Using intermediate certificates is only supported on targets .NET 8 and above.
    /// </remarks>
    public static Func<ValueTask<X509Certificate2Collection>> LoadClientCertFromPem(string certPem, string keyPem)
    {
        var certificateCollection = LoadCertsFromMultiPem(certPem, keyPem);

        return () => ValueTask.FromResult(certificateCollection);
    }

    /// <summary>
    /// Helper method to load CA certificates from a PEM-encoded text.
    /// </summary>
    /// <param name="caPem">Text of PEM-encoded CA certificates</param>
    /// <returns>Returns a callback that will return a collection of CA certificates</returns>
    public static Func<ValueTask<X509Certificate2Collection>> LoadCaCertsFromPem(string caPem)
    {
        var caCerts = new X509Certificate2Collection();
        caCerts.ImportFromPem(caPem);
        return () => ValueTask.FromResult(caCerts);
    }

    /// <summary>
    /// Helper method to load a client certificates and its key from PEM-encoded files
    /// </summary>
    internal static Func<ValueTask<X509Certificate2Collection>> LoadClientCertFromPemFile(string certPemFile, string keyPemFile)
    {
        var certPem = File.ReadAllText(certPemFile);
        var keyPem = File.ReadAllText(keyPemFile);
        var certificateCollection = LoadCertsFromMultiPem(certPem, keyPem);

        return () => ValueTask.FromResult(certificateCollection);
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

    /// <summary>
    /// Helper method to load certificates from a PEM-encoded text.
    /// </summary>
    private static X509Certificate2Collection LoadCertsFromMultiPem(ReadOnlySpan<char> certPem, ReadOnlySpan<char> keyPem)
    {
        var multiPemCertificateCollection = new X509Certificate2Collection();
        var addKey = true;

        while (PemEncoding.TryFind(certPem, out var fields))
        {
            X509Certificate2 certificate;

            if (addKey)
            {
                certificate = X509Certificate2.CreateFromPem(certPem, keyPem);
                addKey = false;
            }
            else
            {
                certificate = X509Certificate2.CreateFromPem(certPem);
            }

            multiPemCertificateCollection.Add(certificate);
            certPem = certPem[fields.Location.End..];
        }

        return multiPemCertificateCollection;
    }
}
