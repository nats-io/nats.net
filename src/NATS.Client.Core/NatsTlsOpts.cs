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

    /// <summary>Path to PEM-encoded X509 Certificate</summary>
    public string? CertFile { get; init; }

    /// <summary>Path to PEM-encoded Private Key</summary>
    public string? KeyFile { get; init; }

    /// <summary>Path to PEM-encoded X509 CA Certificate</summary>
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
