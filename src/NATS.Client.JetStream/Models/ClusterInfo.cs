namespace NATS.Client.JetStream.Models;

public record ClusterInfo
{
    /// <summary>
    /// The cluster name
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string Name { get; set; } = default!;

    /// <summary>
    /// The server name of the RAFT leader
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("leader")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string Leader { get; set; } = default!;

    /// <summary>
    /// The members of the RAFT cluster
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("replicas")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public System.Collections.Generic.ICollection<PeerInfo> Replicas { get; set; } = default!;
}
