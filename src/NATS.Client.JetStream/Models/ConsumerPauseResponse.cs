using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.CONSUMER.PAUSE API
/// </summary>
public record ConsumerPauseResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("paused")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
#if NET6_0
    public bool IsPaused { get; set; }
#else
    public bool IsPaused { get; init; }
#endif

    [System.Text.Json.Serialization.JsonPropertyName("pause_until")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
#if NET6_0
    public DateTimeOffset PauseUntil { get; set; }
#else
    public DateTimeOffset PauseUntil { get; init; }
#endif

    [System.Text.Json.Serialization.JsonPropertyName("pause_remaining")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.Text.Json.Serialization.JsonConverter(typeof(NatsJSJsonNullableNanosecondsConverter))]
#if NET6_0
    public TimeSpan? PauseRemaining { get; set; }
#else
    public TimeSpan? PauseRemaining { get; init; }
#endif
}
