namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.STREAM.PEER.REMOVE API
/// </summary>

public record StreamRemovePeerRequest
{
    /// <summary>
    /// Server name of the peer to remove
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("peer")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [System.ComponentModel.DataAnnotations.Required]
    public string Peer { get; set; } = default!;
}
