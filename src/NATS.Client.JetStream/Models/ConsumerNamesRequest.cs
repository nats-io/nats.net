namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.CONSUMER.NAMES API
/// </summary>

public record ConsumerNamesRequest : IterableRequest
{
    /// <summary>
    /// Filter the names to those consuming messages matching this subject or wildcard
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string Subject { get; set; } = default!;
}
