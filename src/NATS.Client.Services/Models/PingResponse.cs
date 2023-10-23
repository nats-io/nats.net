using System.Text.Json.Serialization;

namespace NATS.Client.Services.Models;

public class PingResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "io.nats.micro.v1.ping_response";

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("version")]
    public string Version { get; set; } = default!;

    [JsonPropertyName("metadata")]
    public IDictionary<string, string> Metadata { get; set; } = default!;
}
