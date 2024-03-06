using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.CONSUMER.PAUSE API
/// </summary>
public record ConsumerPauseResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("paused")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public bool Paused { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("pause_until")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset PauseUntil { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("pause_remaining")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNullableNanosecondsConverter))]
    public TimeSpan? PauseRemaining { get; set; }
}
