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
    public ICollection<ConsumerInfo> Consumers { get; set; } = new System.Collections.ObjectModel.Collection<ConsumerInfo>();

    /// <summary>
    /// In clustered environments gathering Consumer info might time out, this list would be a list of Consumers for which information was not obtainable
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("missing")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<string>? Missing { get; set; }

    /// <summary>
    /// Consumers that are offline, keyed by Consumer name with the reason as the value. A Consumer goes offline when
    /// it requires a higher API level than the server supports. Offline Consumers are also listed in <see cref="Missing"/>.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("offline")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public IDictionary<string, string>? Offline { get; set; }
}
