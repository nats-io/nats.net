namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.META.LEADER.STEPDOWN API
/// </summary>

public record MetaLeaderStepdownRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("placement")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public Placement? Placement { get; set; }
}
