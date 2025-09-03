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

    // TODO: These are 2.12 specific
    // LeaderSince *time.Time  `json:"leader_since,omitempty"`
    // SystemAcc   bool        `json:"system_account,omitempty"`
    // TrafficAcc  string      `json:"traffic_account,omitempty"`
}
