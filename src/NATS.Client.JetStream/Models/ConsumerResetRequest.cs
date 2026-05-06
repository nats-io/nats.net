namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.CONSUMER.RESET API
/// </summary>
/// <remarks>This feature is only available on NATS server v2.14 and later.</remarks>
internal record ConsumerResetRequest
{
    /// <summary>
    /// Stream sequence to reset the consumer to. Zero (the default) is sent as an empty body
    /// and resets the consumer to its current ack floor.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(ulong.MinValue, ulong.MaxValue)]
    public ulong Seq { get; set; }
}
