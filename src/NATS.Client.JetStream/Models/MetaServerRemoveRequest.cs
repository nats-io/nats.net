namespace NATS.Client.JetStream.Models;

/// <summary>
/// A request to the JetStream $JS.API.SERVER.REMOVE API
/// </summary>

public record MetaServerRemoveRequest
{
    /// <summary>
    /// The Name of the server to remove from the meta group
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("peer")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? Peer { get; set; }

    /// <summary>
    /// Peer ID of the peer to be removed. If specified this is used instead of the server name
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("peer_id")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public string? PeerId { get; set; }
}
