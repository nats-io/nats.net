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
}
