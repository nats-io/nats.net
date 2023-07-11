using System.Text.Json.Serialization;

namespace NATS.Client.JetStream.Models;

public record Cluster
{
    [JsonPropertyName("leader")]
    public string Leader { get; set; }
}
