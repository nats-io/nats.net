namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.STREAM.RESTORE API
/// </summary>

public record StreamRestoreResponse
{
    /// <summary>
    /// The Subject to send restore chunks to
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("deliver_subject")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public string DeliverSubject { get; set; } = default!;
}
