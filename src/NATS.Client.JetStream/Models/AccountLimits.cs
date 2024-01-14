namespace NATS.Client.JetStream.Models;

public record AccountLimits
{
    /// <summary>
    /// The maximum amount of Memory storage Stream Messages may consume
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_memory")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(-1, int.MaxValue)]
    public int MaxMemory { get; set; }

    /// <summary>
    /// The maximum amount of File storage Stream Messages may consume
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_storage")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(-1, int.MaxValue)]
    public int MaxStorage { get; set; }

    /// <summary>
    /// The maximum number of Streams an account can create
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_streams")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(-1, int.MaxValue)]
    public int MaxStreams { get; set; }

    /// <summary>
    /// The maximum number of Consumer an account can create
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_consumers")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(-1, int.MaxValue)]
    public int MaxConsumers { get; set; }

    /// <summary>
    /// Indicates if Streams created in this account requires the max_bytes property set
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_bytes_required")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool MaxBytesRequired { get; set; }

    /// <summary>
    /// The maximum number of outstanding ACKs any consumer may configure
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_ack_pending")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int MaxAckPending { get; set; }

    /// <summary>
    /// The maximum size any single memory stream may be
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("memory_max_stream_bytes")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-1, int.MaxValue)]
    public int MemoryMaxStreamBytes { get; set; } = -1;

    /// <summary>
    /// The maximum size any single storage based stream may be
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("storage_max_stream_bytes")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-1, int.MaxValue)]
    public int StorageMaxStreamBytes { get; set; } = -1;
}
