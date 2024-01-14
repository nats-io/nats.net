namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.STREAM.SNAPSHOT API
/// </summary>

public record StreamSnapshotRequest
{
    /// <summary>
    /// The NATS subject where the snapshot will be delivered
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("deliver_subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
#if NET6_0
    public string DeliverSubject { get; set; } = default!;
#else
    public required string DeliverSubject { get; set; }
#endif

    /// <summary>
    /// When true consumer states and configurations will not be present in the snapshot
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("no_consumers")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool NoConsumers { get; set; }

    /// <summary>
    /// The size of data chunks to send to deliver_subject
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("chunk_size")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long ChunkSize { get; set; }

    /// <summary>
    /// Check all message's checksums prior to snapshot
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("jsck")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Jsck { get; set; }
}
