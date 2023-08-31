namespace NATS.Client.Core;

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

    /// <summary>When true, disable TLS</summary>
    public bool Disabled { get; init; }

    /// <summary>When true, skip remote certificate verification and accept any server certificate</summary>
    public bool InsecureSkipVerify { get; init; }

    internal bool Required => CertFile != default || KeyFile != default || CaFile != default;
}
