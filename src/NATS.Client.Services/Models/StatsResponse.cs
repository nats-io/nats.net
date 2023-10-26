using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NATS.Client.Services.Models;

public record StatsResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "io.nats.micro.v1.stats_response";

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("version")]
    public string Version { get; set; } = default!;

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IDictionary<string, string> Metadata { get; set; } = default!;

    [JsonPropertyName("endpoints")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<EndpointStats> Endpoints { get; set; } = default!;

    [JsonPropertyName("started")]
    public string Started { get; set; } = default!;
}

public record EndpointStats
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = default!;

    [JsonPropertyName("queue_group")]
    public string QueueGroup { get; set; } = default!;

    [JsonPropertyName("num_requests")]
    public long NumRequests { get; set; }

    [JsonPropertyName("num_errors")]
    public long NumErrors { get; set; }

    [JsonPropertyName("last_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string LastError { get; set; } = default!;

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonNode Data { get; set; } = default!;

    [JsonPropertyName("processing_time")]
    public long ProcessingTime { get; set; }

    [JsonPropertyName("average_processing_time")]
    public long AverageProcessingTime { get; set; }
}
