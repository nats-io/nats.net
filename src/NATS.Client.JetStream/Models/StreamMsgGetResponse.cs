namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.STREAM.MSG.GET API
/// </summary>

public record StreamMsgGetResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("message")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
#if NET6_0
    public StoredMessage Message { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required StoredMessage Message { get; set; }
#pragma warning restore SA1206
#endif
}
