namespace NATS.Client.JetStream.Models;

public record IterableRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("offset")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Offset { get; set; } = default!;
}
