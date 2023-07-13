namespace NATS.Client.JetStream.Models;

public record SequenceInfo
{
    /// <summary>
    /// The sequence number of the Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("consumer_seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long ConsumerSeq { get; set; } = default!;

    /// <summary>
    /// The sequence number of the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("stream_seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long StreamSeq { get; set; } = default!;

    /// <summary>
    /// The last time a message was delivered or acknowledged (for ack_floor)
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("last_active")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public System.DateTimeOffset LastActive { get; set; } = default!;
}
