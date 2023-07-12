using System.Text.Json.Serialization;

namespace NATS.Client.JetStream.Models;

public record StreamCreateResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("config")]
    public StreamConfig Config { get; set; }

    [JsonPropertyName("created")]
    public DateTimeOffset Created { get; set; }

    [JsonPropertyName("state")]
    public StreamState State { get; set; }

    [JsonPropertyName("did_create")]
    public bool DidCreate { get; set; }
}
