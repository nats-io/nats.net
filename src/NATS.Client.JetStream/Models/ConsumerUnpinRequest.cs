namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.CONSUMER.UNPIN API
/// </summary>
/// <remarks>This feature is only available on NATS server v2.11 and later.</remarks>
internal record ConsumerUnpinRequest
{
    /// <summary>
    /// The priority group name to unpin.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("group")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Group { get; set; }
}
