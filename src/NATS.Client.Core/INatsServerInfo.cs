namespace NATS.Client.Core;

public interface INatsServerInfo
{
    string Id { get; }

    string Name { get; }

    string Version { get; }

    long ProtocolVersion { get; }

    string GitCommit { get; }

    string GoVersion { get; }

    string Host { get; }

    int Port { get; }

    bool HeadersSupported { get; }

    bool AuthRequired { get; }

    bool TlsRequired { get; }

    bool TlsVerify { get; }

    bool TlsAvailable { get; }

    int MaxPayload { get; }

    bool JetStreamAvailable { get; }

    ulong ClientId { get; }

    string ClientIp { get; }

    string? Nonce { get; }

    string? Cluster { get; }

    bool ClusterDynamic { get; }

    string[]? ClientConnectUrls { get; }

    string[]? WebSocketConnectUrls { get; }

    bool LameDuckMode { get; }
}
