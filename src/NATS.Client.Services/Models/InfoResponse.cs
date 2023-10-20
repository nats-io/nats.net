using System.Text.Json.Serialization;

namespace NATS.Client.Services.Models;

public class InfoResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "io.nats.micro.v1.info_response";

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("version")]
    public string Version { get; set; } = default!;

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IDictionary<string, string> Metadata { get; set; } = default!;

    [JsonPropertyName("description")]
    public string Description { get; set; } = default!;

    [JsonPropertyName("endpoints")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<EndpointInfo> Endpoints { get; set; } = default!;
}

public class EndpointInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = default!;

    [JsonPropertyName("queue_group")]
    public string QueueGroup { get; set; } = default!;

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IDictionary<string, string> Metadata { get; set; } = default!;
}
