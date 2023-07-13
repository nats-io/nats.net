namespace NATS.Client.JetStream.Models;

public record StreamState
{
    /// <summary>
    /// Number of messages stored in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("messages")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long Messages { get; set; } = default!;

    /// <summary>
    /// Combined size of all messages in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("bytes")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long Bytes { get; set; } = default!;

    /// <summary>
    /// Sequence number of the first message in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("first_seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long FirstSeq { get; set; } = default!;

    /// <summary>
    /// The timestamp of the first message in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("first_ts")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string FirstTs { get; set; } = default!;

    /// <summary>
    /// Sequence number of the last message in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("last_seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long LastSeq { get; set; } = default!;

    /// <summary>
    /// The timestamp of the last message in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("last_ts")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string LastTs { get; set; } = default!;

    /// <summary>
    /// IDs of messages that were deleted using the Message Delete API or Interest based streams removing messages out of order
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("deleted")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public System.Collections.Generic.ICollection<long> Deleted { get; set; } = default!;

    /// <summary>
    /// Subjects and their message counts when a subjects_filter was set
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("subjects")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public System.Collections.Generic.IDictionary<string, long> Subjects { get; set; } = default!;

    /// <summary>
    /// The number of unique subjects held in the stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_subjects")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long NumSubjects { get; set; } = default!;

    /// <summary>
    /// The number of deleted messages
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("num_deleted")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long NumDeleted { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("lost")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public LostStreamData Lost { get; set; } = default!;

    /// <summary>
    /// Number of Consumers attached to the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("consumer_count")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(-9223372036854776000D, 9223372036854776000D)]
    public long ConsumerCount { get; set; } = default!;
}
