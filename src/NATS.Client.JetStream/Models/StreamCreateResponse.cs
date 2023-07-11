using System.Text.Json.Serialization;

namespace NATS.Client.JetStream.Models;

public record StreamCreateResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("config")]
    public Config Config { get; set; }

    [JsonPropertyName("created")]
    public DateTimeOffset Created { get; set; }

    [JsonPropertyName("state")]
    public State State { get; set; }

    [JsonPropertyName("did_create")]
    public bool DidCreate { get; set; }
}
