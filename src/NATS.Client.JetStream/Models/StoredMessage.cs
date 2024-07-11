namespace NATS.Client.JetStream.Models;

public record StoredMessage
{
    /// <summary>
    /// The subject the message was originally received on
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
#if NET6_0
    public string Subject { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required string Subject { get; set; }
#pragma warning restore SA1206
#endif

    /// <summary>
    /// The sequence number of the message in the Stream
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("seq")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Range(ulong.MinValue, ulong.MaxValue)]
    public ulong Seq { get; set; }

    /// <summary>
    /// The base64 encoded payload of the message body
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("data")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    //[System.ComponentModel.DataAnnotations.StringLength(int.MaxValue)]
    public ReadOnlyMemory<byte> Data { get; set; }

    /// <summary>
    /// The time the message was received
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("time")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
#if NET6_0
    public string Time { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required string Time { get; set; }
#pragma warning restore SA1206
#endif

    /// <summary>
    /// Base64 encoded headers for the message
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("hdrs")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Hdrs { get; set; }
}
