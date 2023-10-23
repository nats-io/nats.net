using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NATS.Client.Services.Models;

public class StatsResponse
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

public class EndpointStats
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

    /**
    * A field that can be customized with any data as returned by stats handler see {@link ServiceConfig}
    */
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonNode Data { get; set; } = default!;

    [JsonPropertyName("processing_time")]
    public long ProcessingTime { get; set; }

    /**
    * Average processing_time is the total processing_time divided by the num_requests
    */
    [JsonPropertyName("average_processing_time")]
    public long AverageProcessingTime { get; set; }
}
