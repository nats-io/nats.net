using NATS.Client.JetStream.Internal;

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
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNanosecondsConverter))]
    public TimeSpan Expires { get; set; }

    /// <summary>
    /// How many messages the server should deliver to the requestor
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("batch")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long Batch { get; set; }

    /// <summary>
    /// Sends at most this many bytes to the requestor, limited by consumer configuration max_bytes
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_bytes")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long MaxBytes { get; set; }

    /// <summary>
    /// When true a response with a 404 status header will be returned when no messages are available
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("no_wait")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool NoWait { get; set; }

    /// <summary>
    /// When not 0 idle heartbeats will be sent on this interval
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("idle_heartbeat")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNanosecondsConverter))]
    public TimeSpan IdleHeartbeat { get; set; }

    /// <summary>
    /// Priority group.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("group")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Group { get; set; }

    /// <summary>
    /// Priority group minimum pending messages.
    /// </summary>
    /// <remarks>
    /// When specified, this Pull request will only receive messages when the consumer has at least this many pending messages.
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("min_pending")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long MinPending { get; set; }

    /// <summary>
    /// Priority group minimum ACK pending messages.
    /// </summary>
    /// <remarks>
    /// When specified, this Pull request will only receive messages when the consumer has at least this many ack pending messages.
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("min_ack_pending")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long MinAckPending { get; set; }

    /// <summary>
    /// Priority group ID.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Id { get; set; }

    /// <summary>
    /// Priority for message delivery when using prioritized priority policy.
    /// </summary>
    /// <remarks>
    /// Lower values indicate higher priority (0 is the highest priority).
    /// Maximum priority value is 9. This field is only used when the consumer
    /// has PriorityPolicy set to "prioritized".
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("priority")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0, 9)]
    public byte Priority { get; set; }
}
