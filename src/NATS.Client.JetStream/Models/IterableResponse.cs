namespace NATS.Client.JetStream.Models;

public record IterableResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("total")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Total { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("offset")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Offset { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("limit")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int Limit { get; set; }
}
