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
#if NET6_0
    public string Peer { get; set; } = default!;
#else
#pragma warning disable SA1206
    public required string Peer { get; set; }
#pragma warning restore SA1206
#endif
}
