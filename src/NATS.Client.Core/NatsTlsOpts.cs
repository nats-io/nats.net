using System.Net.Security;
using System.Security.Authentication;
using NATS.Client.Core.Internal;

#if NETSTANDARD2_0
using System.Security.Cryptography.X509Certificates;
#endif

namespace NATS.Client.Core;

/// <summary>
/// TLS mode to use during connection.
/// </summary>
public enum TlsMode
{
    /// <summary>
    /// For connections that use the "nats://" scheme and don't supply Client or CA Certificates - same as <see cref="Prefer"/>.
    /// For connections that use the "tls://" scheme or supply Client or CA Certificates - same as <see cref="Require"/>.
    /// </summary>
    /// <remarks>
    /// When this resolves to <see cref="Prefer"/>, TLS is opportunistic and subject to the same
    /// limitations described in the <see cref="Prefer"/> remarks.
    /// Use the <c>tls://</c> scheme or set <see cref="Require"/> explicitly when TLS is required for security.
    /// </remarks>
    Auto,

    /// <summary>
    /// If the server supports TLS, then use it; otherwise, use plain-text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is an opportunistic TLS mode. Whether the server "supports TLS" is determined
    /// from the server's INFO message, which is received over plaintext TCP before any TLS
    /// upgrade occurs. A man-in-the-middle attacker on the network could forge the INFO
    /// message to strip the TLS flags, causing the client to skip TLS and send credentials
    /// in plaintext. This is inherent to the NATS protocol's connection flow and applies to
    /// all NATS client implementations.
    /// </para>
    /// <para>
    /// If TLS is required for security, use the <c>tls://</c> scheme or set <see cref="TlsMode.Require"/> explicitly.
    /// </para>
    /// </remarks>
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

#if !NETSTANDARD
    /// <summary>
    /// File path to PEM-encoded X509 Client Certificate
    /// </summary>
    /// <remarks>
    /// Must be used in conjunction with <see cref="KeyFile"/>.
    /// Exclusive of <see cref="CertBundleFile"/>.
    /// </remarks>
    public string? CertFile { get; init; }

    /// <summary>
    /// File path to PEM-encoded Private Key
    /// </summary>
    /// <remarks>
    /// If key is password protected use <see cref="KeyFilePassword"/>.
    /// Must be used in conjunction with <see cref="CertFile"/>.
    /// </remarks>
    public string? KeyFile { get; init; }

    /// <summary>
    /// Key file password
    /// </summary>
    public string? KeyFilePassword { get; init; }
#endif

    /// <summary>
    /// File path to PKCS#12 bundle containing X509 Client Certificate and Private Key
    /// </summary>
    /// <remarks>
    /// Use <see cref="CertBundleFilePassword"/> to specify the password for the bundle.
    /// </remarks>
    public string? CertBundleFile { get; init; }

    /// <summary>
    /// Password for the PKCS#12 bundle file
    /// </summary>
    public string? CertBundleFilePassword { get; init; }

    /// <summary>
    /// Callback to configure <see cref="SslClientAuthenticationOptions"/>
    /// </summary>
    public Func<SslClientAuthenticationOptions, ValueTask>? ConfigureClientAuthentication { get; init; }

    /// <summary>
    /// String or file path to PEM-encoded X509 CA Certificate
    /// </summary>
    public string? CaFile { get; init; }

    /// <summary>When true, skip remote certificate verification and accept any server certificate</summary>
    public bool InsecureSkipVerify { get; init; }

    /// <summary>
    /// TLS mode to use during connection.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="TlsMode.Auto"/>. When the effective mode is <see cref="TlsMode.Prefer"/>,
    /// TLS is opportunistic and a network attacker can downgrade the connection to plaintext.
    /// Use the <c>tls://</c> scheme or <see cref="TlsMode.Require"/> when TLS must be guaranteed.
    /// </remarks>
    public TlsMode Mode { get; init; }

    internal bool HasTlsCerts
    {
        get
        {
#if NETSTANDARD
            const bool certOrKeyFile = false;
#else
            var certOrKeyFile = CertFile != default || KeyFile != default;
#endif
            return certOrKeyFile || CertBundleFile != default || CaFile != default || ConfigureClientAuthentication != default;
        }
    }

    internal TlsMode EffectiveMode(Uri uri) => Mode switch
    {
        TlsMode.Auto => HasTlsCerts || uri.Scheme.ToLower() == "tls" ? TlsMode.Require : TlsMode.Prefer,
        _ => Mode,
    };

    internal bool TryTls(Uri uri)
    {
        var effectiveMode = EffectiveMode(uri);
        return effectiveMode is TlsMode.Require or TlsMode.Prefer;
    }

    internal async ValueTask<SslClientAuthenticationOptions> AuthenticateAsClientOptionsAsync(Uri uri)
    {
        if (EffectiveMode(uri) == TlsMode.Disable)
        {
            throw new InvalidOperationException("TLS is not permitted when TlsMode is set to Disable");
        }

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = uri.Host,
#if NETSTANDARD
            EnabledSslProtocols = SslProtocols.Tls12,
#else
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
#endif
        };

        // validation
#if !NETSTANDARD
        switch (this)
        {
            case { CertFile: not null, KeyFile: null } or { KeyFile: not null, CertFile: null }:
                throw new ArgumentException("NatsTlsOpts.CertFile and NatsTlsOpts.KeyFile must both be set");
            case { CertFile: not null, CertBundleFile: not null }:
                throw new ArgumentException("NatsTlsOpts.CertFile and NatsTlsOpts.CertFileBundle are mutually exclusive");
        }
#endif

        if (CaFile != null)
        {
#if NETSTANDARD2_0
            var caPem = File.ReadAllText(CaFile);
#else
            var caPem = await File.ReadAllTextAsync(CaFile).ConfigureAwait(false);
#endif
            options.LoadCaCertsFromPem(caPem);
        }

#if !NETSTANDARD
        if (CertFile != null && KeyFile != null)
        {
            options.LoadClientCertFromPem(
                certPem: await File.ReadAllTextAsync(CertFile).ConfigureAwait(false),
                keyPem: await File.ReadAllTextAsync(KeyFile).ConfigureAwait(false),
                password: KeyFilePassword);
        }
#endif

        if (CertBundleFile != null)
        {
            options.LoadClientCertFromPfxFile(CertBundleFile, password: CertBundleFilePassword);
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
    }
}

#if NETSTANDARD2_0
public class SslClientAuthenticationOptions
{
    public string? TargetHost { get; set; }

    public SslProtocols EnabledSslProtocols { get; set; }

    public X509CertificateCollection? ClientCertificates { get; set; }

    public X509RevocationMode CertificateRevocationCheckMode { get; set; }

    public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }

    public LocalCertificateSelectionCallback? LocalCertificateSelectionCallback { get; set; }
}
#endif
