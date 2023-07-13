namespace NATS.Client.JetStream.Models;

/// <summary>
/// Configuration referencing a stream source in another account or JetStream domain
/// </summary>

public record ExternalStreamSource
{
    /// <summary>
    /// The subject prefix that imports the other account/domain $JS.API.CONSUMER.&gt; subjects
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("api")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
    public string Api { get; set; } = default!;

    /// <summary>
    /// The delivery subject to use for the push consumer
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("deliver")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string Deliver { get; set; } = default!;
}
