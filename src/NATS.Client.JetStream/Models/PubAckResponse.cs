namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response received when publishing a message
/// </summary>

public record PubAckResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ApiError Error { get; set; } = default!;

    /// <summary>
    /// The name of the stream that received the message
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("stream")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public string Stream { get; set; } = default!;

    /// <summary>
    /// If successful this will be the sequence the message is stored at
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long Seq { get; set; } = default!;

    /// <summary>
    /// Indicates that the message was not stored due to the Nats-Msg-Id header and duplicate tracking
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("duplicate")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Duplicate { get; set; } = false;

    /// <summary>
    /// If the Stream accepting the message is in a JetStream server configured for a domain this would be that domain
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("domain")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string Domain { get; set; } = default!;
}
