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
    /// You can use <see cref="NatsTlsOpts"/> to load the certificate from a file
    /// or you can pass the PEM-encoded certificate directly as string. If the value
    /// looks like a PEM-encoded pattern (for example if it starts with "-----BEGIN CERTIFICATE-----")
    /// it would be used as PEM-encoded string otherwise it would be treated as file path.
    /// </remarks>
    public string? CertFile { get; init; }

    /// <summary>
    /// String or file path to PEM-encoded Private Key
    /// </summary>
    /// <remarks>
    /// You can use <see cref="NatsTlsOpts"/> to load the private key from a file
    /// or you can pass the PEM-encoded private key directly as string. If the value
    /// looks like a PEM-encoded pattern (for example if it starts with "-----BEGIN PRIVATE KEY-----")
    /// it would be used as PEM-encoded string otherwise it would be treated as file path.
    /// </remarks>
    public string? KeyFile { get; init; }

    /// <summary>
    /// String or file path to PEM-encoded X509 CA Certificate
    /// </summary>
    /// <remarks>
    /// You can use <see cref="NatsTlsOpts"/> to load the certificate from a file
    /// or you can pass the PEM-encoded certificate directly as string. If the value
    /// looks like a PEM-encoded pattern (for example if it starts with "-----BEGIN CERTIFICATE-----")
    /// it would be used as PEM-encoded string otherwise it would be treated as file path.
    /// </remarks>
    public string? CaFile { get; init; }

    /// <summary>When true, skip remote certificate verification and accept any server certificate</summary>
    public bool InsecureSkipVerify { get; init; }

    /// <summary>TLS mode to use during connection</summary>
    public TlsMode Mode { get; init; }

    internal bool HasTlsFile => CertFile != default || KeyFile != default || CaFile != default;

    internal TlsMode EffectiveMode(NatsUri uri) => Mode switch
    {
        TlsMode.Auto => HasTlsFile || uri.Uri.Scheme.ToLower() == "tls" ? TlsMode.Require : TlsMode.Prefer,
        _ => Mode,
    };

    internal bool TryTls(NatsUri uri)
    {
        var effectiveMode = EffectiveMode(uri);
        return effectiveMode is TlsMode.Require or TlsMode.Prefer;
    }
}
