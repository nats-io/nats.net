using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.STREAM.MSG.GET API
/// </summary>
public record StreamMsgBatchGetRequest
{
    /// <summary>
    /// The maximum amount of messages to be returned for this request
    /// </summary>
    [JsonPropertyName("batch")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [Range(-1, int.MaxValue)]
    public int Batch { get; set; }

    /// <summary>
    /// The maximum amount of returned bytes for this request.
    /// </summary>
    [JsonPropertyName("max_bytes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [Range(-1, int.MaxValue)]
    public int MaxBytes { get; set; }

    /// <summary>
    /// The minimum sequence for returned message
    /// </summary>
    [JsonPropertyName("seq")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [Range(ulong.MinValue, ulong.MaxValue)]
    public ulong MinSequence { get; set; }

    /// <summary>
    /// The minimum start time for returned message
    /// </summary>
    [JsonPropertyName("start_time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset StartTime { get; set; }

    /// <summary>
    /// The subject used filter messages that should be returned
    /// </summary>
    [JsonPropertyName("next_by_subj")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [Required]
#if NET6_0
    public string Subject { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required string Subject { get; set; }

#pragma warning restore SA1206
#endif

    /// <summary>
    /// Return last messages mathing the subjects
    /// </summary>
    [JsonPropertyName("multi_last")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string[] LastBySubjects { get; set; } = [];

    /// <summary>
    /// Return message after sequence
    /// </summary>
    [JsonPropertyName("up_to_seq")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [Range(ulong.MinValue, ulong.MaxValue)]
    public ulong UpToSequence { get; set; }

    /// <summary>
    /// Return message after time
    /// </summary>
    [JsonPropertyName("up_to_time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset UpToTime { get; set; }
}
