using System.Text.Json.Serialization;

namespace NATS.Client.JetStream.Models;

public record State
{
    [JsonPropertyName("messages")]
    public long Messages { get; set; }

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("first_seq")]
    public long FirstSeq { get; set; }

    [JsonPropertyName("first_ts")]
    public DateTimeOffset FirstTs { get; set; }

    [JsonPropertyName("last_seq")]
    public long LastSeq { get; set; }

    [JsonPropertyName("last_ts")]
    public DateTimeOffset LastTs { get; set; }

    [JsonPropertyName("num_subjects")]
    public long NumSubjects { get; set; }

    [JsonPropertyName("consumer_count")]
    public long ConsumerCount { get; set; }
}
