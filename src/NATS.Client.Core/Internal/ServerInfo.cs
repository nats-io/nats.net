using System.Text.Json.Serialization;

namespace NATS.Client.Core.Internal;

// Defined from `type Info struct` in nats-server
// https://github.com/nats-io/nats-server/blob/a23b1b7/server/server.go#L61
internal sealed record ServerInfo : INatsServerInfo
{
    [JsonPropertyName("server_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("server_name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("proto")]
    public long ProtocolVersion { get; set; }

    [JsonPropertyName("git_commit")]
    public string GitCommit { get; set; } = string.Empty;

    [JsonPropertyName("go")]
    public string GoVersion { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("headers")]
    public bool HeadersSupported { get; set; }

    [JsonPropertyName("auth_required")]
    public bool AuthRequired { get; set; }

    [JsonPropertyName("tls_required")]
    public bool TlsRequired { get; set; }

    [JsonPropertyName("tls_verify")]
    public bool TlsVerify { get; set; }

    [JsonPropertyName("tls_available")]
    public bool TlsAvailable { get; set; }

    [JsonPropertyName("max_payload")]
    public int MaxPayload { get; set; }

    [JsonPropertyName("jetstream")]
    public bool JetStreamAvailable { get; set; }

    [JsonPropertyName("client_id")]
    public ulong ClientId { get; set; }

    [JsonPropertyName("client_ip")]
    public string ClientIp { get; set; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string? Nonce { get; set; }

    [JsonPropertyName("cluster")]
    public string? Cluster { get; set; }

    [JsonPropertyName("cluster_dynamic")]
    public bool ClusterDynamic { get; set; }

    [JsonPropertyName("connect_urls")]
    public string[]? ClientConnectUrls { get; set; }

    [JsonPropertyName("ws_connect_urls")]
    public string[]? WebSocketConnectUrls { get; set; }

    [JsonPropertyName("ldm")]
    public bool LameDuckMode { get; set; }
}
