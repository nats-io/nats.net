namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.CONSUMER.LEADER.STEPDOWN API
/// </summary>

public record ConsumerLeaderStepdownResponse
{
    /// <summary>
    /// If the leader successfully stood down
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("success")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public bool Success { get; set; } = false;
}
