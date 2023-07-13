namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.SERVER.REMOVE API
/// </summary>

public record MetaServerRemoveResponse
{
    /// <summary>
    /// If the peer was successfully removed
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("success")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public bool Success { get; set; } = false;
}
