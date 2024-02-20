namespace NATS.Client.JetStream.Models;

public record StreamState
{
    /// <summary>
    /// Number of messages stored in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("messages")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(ulong.MinValue, ulong.MaxValue)]
    public long Messages { get; set; }

    /// <summary>
    /// Combined size of all messages in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("bytes")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(ulong.MinValue, ulong.MaxValue)]
    public long Bytes { get; set; }

    /// <summary>
    /// Sequence number of the first message in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("first_seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(ulong.MinValue, ulong.MaxValue)]
    public ulong FirstSeq { get; set; }

    /// <summary>
    /// The timestamp of the first message in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("first_ts")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? FirstTs { get; set; }

    /// <summary>
    /// Sequence number of the last message in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("last_seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(ulong.MinValue, ulong.MaxValue)]
    public ulong LastSeq { get; set; }

    /// <summary>
    /// The timestamp of the last message in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("last_ts")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? LastTs { get; set; }

    /// <summary>
    /// IDs of messages that were deleted using the Message Delete API or Interest based streams removing messages out of order
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("deleted")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ICollection<long>? Deleted { get; set; }

    /// <summary>
    /// Subjects and their message counts when a subjects_filter was set
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("subjects")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public IDictionary<string, long>? Subjects { get; set; }

    /// <summary>
    /// The number of unique subjects held in the stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_subjects")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long NumSubjects { get; set; }

    /// <summary>
    /// The number of deleted messages
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_deleted")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long NumDeleted { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("lost")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public LostStreamData? Lost { get; set; }

    /// <summary>
    /// Number of Consumers attached to the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("consumer_count")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(long.MinValue, long.MaxValue)]
    public long ConsumerCount { get; set; }
}
