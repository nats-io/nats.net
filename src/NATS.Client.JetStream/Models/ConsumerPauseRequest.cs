namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.CONSUMER.PAUSE API
/// </summary>
internal record ConsumerPauseRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("pause_until")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset? PauseUntil { get; set; }
}
