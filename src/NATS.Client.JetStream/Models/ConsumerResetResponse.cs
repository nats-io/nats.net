namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.CONSUMER.RESET API
/// </summary>

public record ConsumerResetResponse : ConsumerInfo
{
    /// <summary>
    /// The stream sequence the consumer was reset to. The next delivered message will have a stream sequence >= ResetSeq.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("reset_seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(ulong.MinValue, ulong.MaxValue)]
    public ulong ResetSeq { get; set; }
}
