namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.STREAM.MSG.GET API
/// </summary>

public record StreamMsgGetResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("message")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public StoredMessage Message { get; set; } = new StoredMessage();
}
