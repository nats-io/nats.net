namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.STREAM.PURGE API
/// </summary>

public record StreamPurgeRequest
{
    /// <summary>
    /// Restrict purging to messages that match this subject
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("filter")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Filter { get; set; }

    /// <summary>
    /// Purge all messages up to but not including the message with this sequence. Can be combined with subject filter but not the keep option
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long Seq { get; set; }

    /// <summary>
    /// Ensures this many messages are present after the purge. Can be combined with the subject filter but not the sequence
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("keep")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long Keep { get; set; }
}
