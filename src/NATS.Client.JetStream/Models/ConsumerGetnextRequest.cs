namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.CONSUMER.MSG.NEXT API
/// </summary>

public record ConsumerGetnextRequest
{
    /// <summary>
    /// A duration from now when the pull should expire, stated in nanoseconds, 0 for no expiry
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("expires")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long Expires { get; set; } = default!;

    /// <summary>
    /// How many messages the server should deliver to the requestor
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("batch")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long Batch { get; set; } = default!;

    /// <summary>
    /// Sends at most this many bytes to the requestor, limited by consumer configuration max_bytes
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_bytes")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long MaxBytes { get; set; } = default!;

    /// <summary>
    /// When true a response with a 404 status header will be returned when no messages are available
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("no_wait")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool NoWait { get; set; } = default!;

    /// <summary>
    /// When not 0 idle heartbeats will be sent on this interval
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("idle_heartbeat")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long IdleHeartbeat { get; set; } = default!;
}
