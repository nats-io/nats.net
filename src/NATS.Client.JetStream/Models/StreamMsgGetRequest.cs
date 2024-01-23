namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.STREAM.MSG.GET API
/// </summary>

public record StreamMsgGetRequest
{
    /// <summary>
    /// Stream sequence number of the message to retrieve, cannot be combined with last_by_subj
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ulong Seq { get; set; }

    /// <summary>
    /// Retrieves the last message for a given subject, cannot be combined with seq
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("last_by_subj")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? LastBySubj { get; set; }

    /// <summary>
    /// Combined with sequence gets the next message for a subject with the given sequence or higher
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("next_by_subj")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? NextBySubj { get; set; }
}
