using System.Text.Json.Serialization;

namespace NATS.Client.JetStream.Models;

public record StreamInfoResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("total")]
    public long Total { get; set; }

    [JsonPropertyName("offset")]
    public long Offset { get; set; }

    [JsonPropertyName("limit")]
    public long Limit { get; set; }

    [JsonPropertyName("config")]
    public Config Config { get; set; }

    [JsonPropertyName("created")]
    public DateTimeOffset Created { get; set; }

    [JsonPropertyName("state")]
    public State State { get; set; }

    [JsonPropertyName("cluster")]
    public Cluster Cluster { get; set; }
}
