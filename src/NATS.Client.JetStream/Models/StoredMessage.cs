namespace NATS.Client.JetStream.Models;

public record StoredMessage
{
    /// <summary>
    /// The subject the message was originally received on
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public string Subject { get; set; } = default!;

    /// <summary>
    /// The sequence number of the message in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(0D, 18446744073709552000D)]
    public long Seq { get; set; } = default!;

    /// <summary>
    /// The base64 encoded payload of the message body
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("data")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [System.ComponentModel.DataAnnotations.StringLength(int.MaxValue)]
    public string Data { get; set; } = default!;

    /// <summary>
    /// The time the message was received
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("time")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
    public string Time { get; set; } = default!;

    /// <summary>
    /// Base64 encoded headers for the message
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("hdrs")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string Hdrs { get; set; } = default!;
}
