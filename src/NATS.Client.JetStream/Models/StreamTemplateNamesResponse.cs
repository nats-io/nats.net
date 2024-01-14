namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.STREAM.TEMPLATE.NAMES API
/// </summary>

public record StreamTemplateNamesResponse : IterableResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("consumers")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<string>? Consumers { get; set; }
}
