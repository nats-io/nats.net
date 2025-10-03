namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response received when publishing a message
/// </summary>

public record PubAckResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ApiError? Error { get; set; }

    /// <summary>
    /// The name of the stream that received the message
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("stream")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public string? Stream { get; set; }

    /// <summary>
    /// If successful this will be the sequence the message is stored at
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.Range(ulong.MinValue, ulong.MaxValue)]
    public ulong Seq { get; set; }

    /// <summary>
    /// Indicates that the message was not stored due to the Nats-Msg-Id header and duplicate tracking
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("duplicate")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Duplicate { get; set; }

    /// <summary>
    /// If the Stream accepting the message is in a JetStream server configured for a domain this would be that domain
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("domain")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Domain { get; set; }

    /// <summary>
    /// Contains the current counter value when publishing messages with counter headers.
    /// This property is used in the context of the message counter feature.
    /// </summary>
    /// <remarks>Supported by server v2.12</remarks>
    [System.Text.Json.Serialization.JsonPropertyName("val")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Value { get; set; }
}
