using System.Net.Security;
using System.Security.Authentication;
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
    /// Callback to configure <see cref="SslClientAuthenticationOptions"/>
    /// </summary>
    public Func<SslClientAuthenticationOptions, ValueTask>? ConfigureClientAuthentication { get; init; }

    /// <summary>
    /// Callback that loads Client Certificate
    /// </summary>
    /// <remarks>
    /// Obsolete, use <see cref="ConfigureClientAuthentication"/> instead
    /// </remarks>
    [Obsolete("use ConfigureClientAuthentication")]
    public Func<ValueTask<X509Certificate2>>? LoadClientCert { get; init; }

    /// <summary>
    /// String or file path to PEM-encoded X509 CA Certificate
    /// </summary>
    public string? CaFile { get; init; }

    /// <summary>
    /// Callback that loads CA Certificates
    /// </summary>
    /// <remarks>
    /// Obsolete, use <see cref="ConfigureClientAuthentication"/> instead
    /// </remarks>
    [Obsolete("use ConfigureClientAuthentication")]
    public Func<ValueTask<X509Certificate2Collection>>? LoadCaCerts { get; init; }

    /// <summary>When true, skip remote certificate verification and accept any server certificate</summary>
    public bool InsecureSkipVerify { get; init; }

    /// <summary>Certificate revocation mode for certificate validation.</summary>
    /// <value>One of the values in <see cref="T:System.Security.Cryptography.X509Certificates.X509RevocationMode" />. The default is <see langword="NoCheck" />.</value>
    /// <remarks>
    /// Obsolete, use <see cref="ConfigureClientAuthentication"/> instead
    /// </remarks>
    [Obsolete("use ConfigureClientAuthentication")]
    public X509RevocationMode CertificateRevocationCheckMode { get; init; }

    /// <summary>TLS mode to use during connection</summary>
    public TlsMode Mode { get; init; }

    internal bool HasTlsCerts => CertFile != default || KeyFile != default || CaFile != default || ConfigureClientAuthentication != default;

    /// <summary>
    /// Helper method to load a Client Certificate from a pem-encoded string
    /// </summary>
    /// <remarks>
    /// Obsolete, use <see cref="ConfigureClientAuthentication"/> instead
    /// </remarks>
    [Obsolete("use ConfigureClientAuthentication")]
    public static Func<ValueTask<X509Certificate2>> LoadClientCertFromPem(string certPem, string keyPem)
    {
        var clientCert = X509Certificate2.CreateFromPem(certPem, keyPem);
        return () => ValueTask.FromResult(clientCert);
    }

    /// <summary>
    /// Helper method to load CA Certificates from a pem-encoded string
    /// </summary>
    /// <remarks>
    /// Obsolete, use <see cref="ConfigureClientAuthentication"/> instead
    /// </remarks>
    [Obsolete("use ConfigureClientAuthentication")]
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

    internal async ValueTask<SslClientAuthenticationOptions> AuthenticateAsClientOptionsAsync(NatsUri uri)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        if (EffectiveMode(uri) == TlsMode.Disable)
        {
            throw new InvalidOperationException("TLS is not permitted when TlsMode is set to Disable");
        }

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = uri.Host,

            CertificateRevocationCheckMode = CertificateRevocationCheckMode,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        };

        // validation
        switch (this)
        {
        case { CertFile: not null, KeyFile: null } or { KeyFile: not null, CertFile: null }:
            throw new ArgumentException("NatsTlsOpts.CertFile and NatsTlsOpts.KeyFile must both be set");
        case { CertFile: not null, KeyFile: not null, LoadClientCert: not null }:
            throw new ArgumentException("NatsTlsOpts.CertFile/KeyFile and NatsTlsOpts.LoadClientCert cannot both be set");
        case { CaFile: not null, LoadCaCerts: not null }:
            throw new ArgumentException("NatsTlsOpts.CaFile and NatsTlsOpts.LoadCaCerts cannot both be set");
        }

        if (CaFile != null)
        {
            options.LoadCaCertsFromPem(await File.ReadAllTextAsync(CaFile).ConfigureAwait(false));
        }

        if (LoadCaCerts != null)
        {
            options.LoadCaCertsFromX509(await LoadCaCerts().ConfigureAwait(false));
        }

        if (CertFile != null && KeyFile != null)
        {
            options.LoadClientCertFromPem(
                await File.ReadAllTextAsync(CertFile).ConfigureAwait(false),
                await File.ReadAllTextAsync(KeyFile).ConfigureAwait(false));
        }

        if (LoadClientCert != null)
        {
            options.LoadClientCertFromX509(await LoadClientCert().ConfigureAwait(false));
        }

        if (InsecureSkipVerify)
        {
            options.InsecureSkipVerify();
        }

        if (ConfigureClientAuthentication != null)
        {
            await ConfigureClientAuthentication(options).ConfigureAwait(false);
        }

        return options;
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
