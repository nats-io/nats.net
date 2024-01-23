namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.CONSUMER.DELETE API
/// </summary>

public record ConsumerDeleteResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("success")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public bool Success { get; set; }
}
