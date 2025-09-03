namespace NATS.Client.JetStream.Models;

public record ClusterInfo
{
    /// <summary>
    /// The cluster name
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Name { get; set; }

    /// <summary>
    /// RAFT group name
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("raft_group")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? RaftGroup { get; set; }

    /// <summary>
    /// The server name of the RAFT leader
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("leader")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Leader { get; set; }

    /// <summary>
    /// The members of the RAFT cluster
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("replicas")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<PeerInfo>? Replicas { get; set; }

    /// <summary>
    /// Represents the duration of time since the cluster leader was elected.
    /// </summary>
    /// <remarks>Supported by server v2.12</remarks>
    [System.Text.Json.Serialization.JsonPropertyName("leader_since")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
#if NET6_0
    public DateTimeOffset LeaderSince { get; set; }
#else
    public DateTimeOffset LeaderSince { get; init; }
#endif

    /// <summary>
    /// Indicates whether the account is a system account.
    /// </summary>
    /// <remarks>Supported by server v2.12</remarks>
    [System.Text.Json.Serialization.JsonPropertyName("system_account")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
#if NET6_0
    public bool SystemAccount { get; set; }
#else
    public bool SystemAccount { get; init; }
#endif

    /// <summary>
    /// The traffic account associated with the cluster.
    /// </summary>
    /// <remarks>Supported by server v2.12</remarks>
    [System.Text.Json.Serialization.JsonPropertyName("traffic_account")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
#if NET6_0
    public string? TrafficAccount { get; set; }
#else
    public string? TrafficAccount { get; init; }
#endif
}
