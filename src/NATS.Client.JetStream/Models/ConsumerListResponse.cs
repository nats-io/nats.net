namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.CONSUMER.LIST API
/// </summary>

public record ConsumerListResponse : IterableResponse
{
    /// <summary>
    /// Full Consumer information for each known Consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("consumers")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public System.Collections.Generic.ICollection<ConsumerInfo> Consumers { get; set; } = new System.Collections.ObjectModel.Collection<ConsumerInfo>();
}
