using System.Text.Json.Serialization;

namespace NATS.Client.Core.Internal;

// Defined from `type Info struct` in nats-server
// https://github.com/nats-io/nats-server/blob/a23b1b7/server/server.go#L61
internal sealed record ServerInfo : INatsServerInfo
{
    [JsonPropertyName("server_id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("server_name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("proto")]
    public long ProtocolVersion { get; init; }

    [JsonPropertyName("git_commit")]
    public string GitCommit { get; init; } = string.Empty;

    [JsonPropertyName("go")]
    public string GoVersion { get; init; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; init; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("headers")]
    public bool HeadersSupported { get; init; }

    [JsonPropertyName("auth_required")]
    public bool AuthRequired { get; init; }

    [JsonPropertyName("tls_required")]
    public bool TlsRequired { get; init; }

    [JsonPropertyName("tls_verify")]
    public bool TlsVerify { get; init; }

    [JsonPropertyName("tls_available")]
    public bool TlsAvailable { get; init; }

    [JsonPropertyName("max_payload")]
    public int MaxPayload { get; init; }

    [JsonPropertyName("jetstream")]
    public bool JetStreamAvailable { get; init; }

    [JsonPropertyName("client_id")]
    public ulong ClientId { get; init; }

    [JsonPropertyName("client_ip")]
    public string ClientIp { get; init; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string? Nonce { get; init; }

    [JsonPropertyName("cluster")]
    public string? Cluster { get; init; }

    [JsonPropertyName("cluster_dynamic")]
    public bool ClusterDynamic { get; init; }

    [JsonPropertyName("connect_urls")]
    public string[]? ClientConnectUrls { get; init; }

    [JsonPropertyName("ws_connect_urls")]
    public string[]? WebSocketConnectUrls { get; init; }

    [JsonPropertyName("ldm")]
    public bool LameDuckMode { get; init; }
}
