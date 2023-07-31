using System.Text.Json.Serialization;

namespace NATS.Client.Core;

// Defined from `type Info struct` in nats-server
// https://github.com/nats-io/nats-server/blob/a23b1b7/server/server.go#L61
public sealed record ServerInfo
{
    [JsonPropertyName("server_id")]
    public string Id { get; internal set; } = string.Empty;

    [JsonPropertyName("server_name")]
    public string Name { get; internal set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; internal set; } = string.Empty;

    [JsonPropertyName("proto")]
    public long ProtocolVersion { get; internal set; }

    [JsonPropertyName("git_commit")]
    public string GitCommit { get; internal set; } = string.Empty;

    [JsonPropertyName("go")]
    public string GoVersion { get; internal set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; internal set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; internal set; }

    [JsonPropertyName("headers")]
    public bool HeadersSupported { get; internal set; }

    [JsonPropertyName("auth_required")]
    public bool AuthRequired { get; internal set; }

    [JsonPropertyName("tls_required")]
    public bool TlsRequired { get; internal set; }

    [JsonPropertyName("tls_verify")]
    public bool TlsVerify { get; internal set; }

    [JsonPropertyName("tls_available")]
    public bool TlsAvailable { get; internal set; }

    [JsonPropertyName("max_payload")]
    public int MaxPayload { get; internal set; }

    [JsonPropertyName("jetstream")]
    public bool JetStreamAvailable { get; internal set; }

    [JsonPropertyName("client_id")]
    public ulong ClientId { get; internal set; }

    [JsonPropertyName("client_ip")]
    public string ClientIp { get; internal set; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string? Nonce { get; internal set; }

    [JsonPropertyName("cluster")]
    public string? Cluster { get; internal set; }

    [JsonPropertyName("cluster_dynamic")]
    public bool ClusterDynamic { get; internal set; }

    [JsonPropertyName("connect_urls")]
    public string[]? ClientConnectUrls { get; internal set; }

    [JsonPropertyName("ws_connect_urls")]
    public string[]? WebSocketConnectUrls { get; internal set; }

    [JsonPropertyName("ldm")]
    public bool LameDuckMode { get; internal set; }
}
