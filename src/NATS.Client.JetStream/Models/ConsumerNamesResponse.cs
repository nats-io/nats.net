namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.CONSUMER.NAMES API
/// </summary>

public record ConsumerNamesResponse : IterableResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("consumers")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public System.Collections.Generic.ICollection<string> Consumers { get; set; } = new System.Collections.ObjectModel.Collection<string>();
}
