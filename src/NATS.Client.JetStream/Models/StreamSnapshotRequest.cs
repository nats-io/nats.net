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
#pragma warning disable SA1206
    public required string DeliverSubject { get; set; }
#pragma warning restore SA1206
#endif

    /// <summary>
    /// When true consumer states and configurations will not be present in the snapshot
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("no_consumers")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool NoConsumers { get; set; }

    /// <summary>
    /// Optional chunk size preference.
    /// Best to just let server select.
    /// Defaults on the server to 128KB, automatically clamped to within the range 1KB to 1MB.
    /// A smaller chunk size means more in-flight messages and more acks needed.
    /// Links with good throughput but high latency may need to increase this.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("chunk_size")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(1024, 1024 * 1024)]
    public int? ChunkSize { get; set; }

    /// <summary>
    /// Check all message's checksums prior to snapshot
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("jsck")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Jsck { get; set; }

    /// <summary>
    /// Optional window size preference.
    /// Defaults on the server to 8MB, automatically clamped to within the range 1KB to 32MB.
    /// Very slow connections may need to reduce this to avoid slow consumer issues.
    /// Minimum Server Version 2.15.5
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("window_size")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(1024, 32 * 1024 * 1024)]
    public int? WindowSize { get; set; }
}
